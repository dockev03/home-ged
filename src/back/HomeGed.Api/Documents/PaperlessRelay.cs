using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Paperless;

namespace HomeGed.Api.Documents;

/// <summary>
/// Appels à Paperless au nom de l'utilisateur connecté : token de sa session, erreurs traduites
/// en réponses Home-GED. Un token refusé par Paperless révoque la session.
/// </summary>
public sealed partial class PaperlessRelay(
    PaperlessClient paperless,
    PaperlessTokenProtector protector,
    ISessionStore store,
    TimeProvider time,
    ILogger<PaperlessRelay> logger)
{
    /// <summary>Nombre maximal de messages d'erreur de Paperless renvoyés au navigateur.</summary>
    private const int MaxErrors = 5;

    /// <summary>
    /// Envoie la requête ; en cas de succès (2xx), <paramref name="onSuccess"/> rend le résultat,
    /// ou null pour laisser l'appelant poursuivre. Sinon, rend l'erreur à renvoyer.
    /// </summary>
    /// <param name="keepResponse">La réponse est relayée en flux : c'est la requête HTTP qui la libère.</param>
    /// <param name="isFilter">Un 400 signale un filtre invalide (lecture) plutôt qu'une modification refusée.</param>
    /// <param name="onError">Traduction propre à la route d'un code d'erreur ; null pour la traduction commune.</param>
    /// <param name="upload">Corps multipart relayé en flux (envoi de fichier) à la place de <paramref name="body"/>.</param>
    public async Task<IResult?> SendAsync(
        HttpContext context,
        HttpMethod method,
        string uri,
        JsonNode? body,
        Func<HttpResponseMessage, CancellationToken, Task<IResult?>> onSuccess,
        CancellationToken cancellationToken,
        bool keepResponse = false,
        bool isFilter = false,
        Func<HttpStatusCode, IResult?>? onError = null,
        HttpContent? upload = null)
    {
        var session = context.Features.Get<CurrentSession>()!.Session;
        HttpResponseMessage? response = null;
        try
        {
            var token = protector.Unprotect(session.PaperlessToken);
            response = upload is null
                ? await paperless.SendAsync(token, method, uri, body, cancellationToken)
                : await paperless.UploadAsync(token, uri, upload, cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized
                && onError?.Invoke(response.StatusCode) is { } specific)
            {
                return specific;
            }

            if (response.IsSuccessStatusCode)
            {
                var result = await onSuccess(response, cancellationToken);
                if (keepResponse)
                {
                    context.Response.RegisterForDispose(response);
                    response = null;
                }

                return result;
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    // Token supprimé ou mot de passe changé dans Paperless : la session ne sert plus à rien.
                    LogTokenRejected(logger, session.Username);
                    await store.RevokeAsync(session.Id, session.UserId, time.GetUtcNow(), cancellationToken);
                    AuthEndpoints.DeleteCookie(context);
                    return Problem(HttpStatusCode.Unauthorized, "Connexion requise.", "non_connecte");

                case HttpStatusCode.Forbidden:
                    return Problem(HttpStatusCode.Forbidden, "Accès refusé par Paperless.", "acces_refuse");

                case HttpStatusCode.NotFound:
                    return Problem(HttpStatusCode.NotFound, "Document introuvable.", "introuvable");

                case HttpStatusCode.BadRequest when isFilter:
                    return Problem(HttpStatusCode.BadRequest, "Filtre ou tri invalide.", "filtre_invalide");

                case HttpStatusCode.BadRequest:
                    var errors = await ReadErrorsAsync(response, cancellationToken);
                    return Problem(
                        HttpStatusCode.BadRequest,
                        errors.Count > 0 ? "Paperless refuse la modification : " + string.Join(" ; ", errors) : "Paperless refuse la modification.",
                        "modification_refusee");

                default:
                    throw new HttpRequestException(
                        $"Réponse inattendue de Paperless (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            && !cancellationToken.IsCancellationRequested)
        {
            LogPaperlessUnavailable(logger, ex);
            return Problem(HttpStatusCode.ServiceUnavailable, "Paperless ne répond pas, réessayez plus tard.", "paperless_indisponible");
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>Relaie le corps JSON de Paperless tel quel.</summary>
    public static async Task<IResult?> JsonBody(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return TypedResults.NoContent();
        }

        var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        return TypedResults.Json(node);
    }

    public static IResult Problem(HttpStatusCode status, string title, string code) =>
        Results.Problem(statusCode: (int)status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// Messages d'une erreur de validation DRF (<c>{"champ": ["message"]}</c>, éventuellement imbriqués),
    /// préfixés par le nom du champ.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        try
        {
            var root = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            Collect(root, null, messages);
        }
        catch (JsonException)
        {
            // Corps non JSON : message générique.
        }

        return messages;
    }

    private static void Collect(JsonNode? node, string? field, List<string> messages)
    {
        if (messages.Count >= MaxErrors)
        {
            return;
        }

        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    // Le champ de premier niveau nomme l'erreur (custom_fields, pas son « value » imbriqué).
                    Collect(value, field ?? (key is "non_field_errors" or "detail" or "error" ? null : key), messages);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item, field, messages);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text) && text.Length > 0:
                var message = text.Length > 200 ? text[..200] : text;
                messages.Add(field is null ? message : $"{field} : {message}");
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Paperless refuse le token de {Username} : session révoquée")]
    private static partial void LogTokenRejected(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Error, Message = "Paperless injoignable")]
    private static partial void LogPaperlessUnavailable(ILogger logger, Exception ex);
}
