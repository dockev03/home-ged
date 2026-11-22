using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Paperless;

namespace HomeGed.Api.Documents;

/// <summary>
/// Relais en lecture vers Paperless pour la liste des documents et ses filtres (UI-02, UI-03).
/// Chaque appel part avec le token de l'utilisateur : Paperless applique ses droits.
/// Seuls les paramètres connus sont transmis.
/// </summary>
public static partial class DocumentEndpoints
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    /// <summary>Paramètres de filtre et de tri acceptés pour <c>GET /api/documents</c>.</summary>
    internal static readonly FrozenSet<string> ListParameters = FrozenSet.Create(
        StringComparer.Ordinal,
        "page", "ordering",
        "query", "title_content", "title__icontains",
        "tags__id__all", "tags__id__in", "is_tagged",
        "correspondent__id__in", "correspondent__isnull",
        "document_type__id__in", "document_type__isnull",
        "storage_path__id__in", "storage_path__isnull",
        "custom_fields__id__all", "custom_fields__icontains",
        "created__gte", "created__lte", "added__date__gte", "added__date__lte",
        "owner__id", "owner__id__none", "owner__isnull");

    /// <summary>Listes de référence (nom côté Home-GED → ressource Paperless et champs renvoyés).</summary>
    internal static readonly FrozenDictionary<string, (string Resource, string[] Fields)> References =
        new Dictionary<string, (string, string[])>(StringComparer.Ordinal)
        {
            ["tags"] = ("tags", ["id", "name", "color", "text_color", "is_inbox_tag"]),
            ["correspondants"] = ("correspondents", ["id", "name"]),
            ["types"] = ("document_types", ["id", "name"]),
            ["chemins"] = ("storage_paths", ["id", "name"]),
            ["champs"] = ("custom_fields", ["id", "name", "data_type"]),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private const int ReferencePageSize = 500;

    /// <summary>Garde-fou : 20 pages de 500 éléments.</summary>
    private const int MaxReferencePages = 20;

    public static RouteGroupBuilder MapDocuments(this RouteGroupBuilder api)
    {
        api.MapGet("/documents", ListAsync);
        api.MapGet("/documents/{id:int}/thumb", ThumbnailAsync);
        api.MapGet("/references/{kind}", ReferencesAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context, PaperlessClient paperless, PaperlessTokenProtector protector, ISessionStore store,
        TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>>();
        foreach (var (key, values) in context.Request.Query)
        {
            if (ListParameters.Contains(key))
            {
                query.AddRange(values.Select(v => new KeyValuePair<string, string?>(key, v)));
            }
        }

        query.Add(new("page_size", PageSize(context.Request.Query["page_size"]).ToString(CultureInfo.InvariantCulture)));
        // Extrait du contenu seulement : la liste n'affiche pas le texte OCR complet.
        query.Add(new("truncate_content", "true"));

        // Le relais rend toujours un résultat quand onSuccess en rend un.
        return (await RelayAsync(
            context, paperless, protector, store, time, loggers,
            "api/documents/" + QueryString.Create(query).ToUriComponent(),
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                // Liens de pagination : ils contiennent l'adresse interne de Paperless.
                body.Remove("next");
                body.Remove("previous");
                return TypedResults.Json(body);
            },
            cancellationToken))!;
    }

    private static async Task<IResult> ThumbnailAsync(
        int id, HttpContext context, PaperlessClient paperless, PaperlessTokenProtector protector, ISessionStore store,
        TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        (await RelayAsync(
            context, paperless, protector, store, time, loggers,
            string.Create(CultureInfo.InvariantCulture, $"api/documents/{id}/thumb/"),
            async (response, ct) =>
            {
                var type = response.Content.Headers.ContentType?.MediaType;
                var stream = await response.Content.ReadAsStreamAsync(ct);
                context.Response.RegisterForDispose(response);
                context.Response.Headers.CacheControl = "private, max-age=600";
                return TypedResults.Stream(
                    stream, type is not null && type.StartsWith("image/", StringComparison.Ordinal) ? type : "application/octet-stream");
            },
            cancellationToken,
            disposeResponse: false))!;

    private static async Task<IResult> ReferencesAsync(
        string kind, HttpContext context, PaperlessClient paperless, PaperlessTokenProtector protector, ISessionStore store,
        TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (!References.TryGetValue(kind, out var reference))
        {
            return Problem(HttpStatusCode.NotFound, "Liste inconnue.", "liste_inconnue");
        }

        var items = new JsonArray();
        for (var page = 1; page <= MaxReferencePages; page++)
        {
            var uri = string.Create(
                CultureInfo.InvariantCulture,
                $"api/{reference.Resource}/?page={page}&page_size={ReferencePageSize}&ordering=name");
            var hasNext = false;
            var result = await RelayAsync(
                context, paperless, protector, store, time, loggers, uri,
                async (response, ct) =>
                {
                    var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
                    foreach (var item in body?["results"]?.AsArray() ?? [])
                    {
                        if (item is JsonObject source)
                        {
                            items.Add(Project(source, reference.Fields));
                        }
                    }

                    hasNext = body?["next"] is JsonValue;
                    return null;
                },
                cancellationToken);
            if (result is not null)
            {
                return result;
            }

            if (!hasNext)
            {
                break;
            }
        }

        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Json(items);
    }

    internal static int PageSize(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            ? Math.Clamp(size, 1, MaxPageSize)
            : DefaultPageSize;

    private static JsonObject Project(JsonObject source, string[] fields)
    {
        var target = new JsonObject();
        foreach (var field in fields)
        {
            if (source.TryGetPropertyValue(field, out var value))
            {
                target[field] = value?.DeepClone();
            }
        }

        return target;
    }

    /// <summary>
    /// Appelle Paperless avec le token de la session et traduit ses erreurs. <paramref name="onSuccess"/>
    /// rend le résultat, ou null pour laisser l'appelant poursuivre.
    /// </summary>
    private static async Task<IResult?> RelayAsync(
        HttpContext context, PaperlessClient paperless, PaperlessTokenProtector protector, ISessionStore store,
        TimeProvider time, ILoggerFactory loggers, string uri,
        Func<HttpResponseMessage, CancellationToken, Task<IResult?>> onSuccess,
        CancellationToken cancellationToken, bool disposeResponse = true)
    {
        var logger = loggers.CreateLogger(typeof(DocumentEndpoints));
        var session = context.Features.Get<CurrentSession>()!.Session;
        HttpResponseMessage? response = null;
        try
        {
            response = await paperless.GetAsync(protector.Unprotect(session.PaperlessToken), uri, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var result = await onSuccess(response, cancellationToken);
                    if (!disposeResponse)
                    {
                        response = null;
                    }

                    return result;

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

                case HttpStatusCode.BadRequest:
                    return Problem(HttpStatusCode.BadRequest, "Filtre ou tri invalide.", "filtre_invalide");

                default:
                    throw new HttpRequestException(
                        $"Réponse inattendue de Paperless (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
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

    private static IResult Problem(HttpStatusCode status, string title, string code) =>
        Results.Problem(statusCode: (int)status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    [LoggerMessage(Level = LogLevel.Warning, Message = "Paperless refuse le token de {Username} : session révoquée")]
    private static partial void LogTokenRejected(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Error, Message = "Paperless injoignable pendant la lecture des documents")]
    private static partial void LogPaperlessUnavailable(ILogger logger, Exception ex);
}
