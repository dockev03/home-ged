using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace HomeGed.Api.Documents;

/// <summary>
/// Corbeille (UI-10) : documents supprimés que Paperless conserve avant de les effacer. Toujours
/// avec le token de l'utilisateur : Paperless ne liste que ses documents (tous pour un
/// superutilisateur) et vérifie le droit de suppression avant de restaurer ou d'effacer.
/// </summary>
public static class TrashEndpoints
{
    public const int PageSize = 25;

    /// <summary>Garde-fou sur le nombre de documents d'une restauration ou d'une suppression.</summary>
    public const int MaxDocuments = 1000;

    /// <summary>Champs du document renvoyés au navigateur : ni contenu OCR ni chemins de fichiers.</summary>
    private static readonly string[] Fields =
        ["id", "title", "correspondent", "document_type", "tags", "created", "deleted_at"];

    public static RouteGroupBuilder MapTrash(this RouteGroupBuilder api)
    {
        api.MapGet("/corbeille", ListAsync);
        api.MapPost("/corbeille/restauration", RestoreAsync);
        api.MapPost("/corbeille/suppression", EmptyAsync);
        return api;
    }

    /// <summary>
    /// Une page de la corbeille, les plus récemment supprimés d'abord, avec le délai de conservation
    /// de Paperless (<c>trash_delay</c>, en jours) pour afficher la date d'effacement automatique.
    /// </summary>
    private static async Task<IResult> ListAsync(HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var page = int.TryParse(context.Request.Query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : 1;

        int? delay = null;
        var failed = await relay.SendAsync(
            context, HttpMethod.Get, "api/ui_settings/", null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
                delay = body?["settings"]?["trash_delay"] is JsonValue v && v.TryGetValue<int>(out var days) && days >= 0 ? days : null;
                return null;
            },
            cancellationToken);
        if (failed is not null)
        {
            return failed;
        }

        var query = QueryString.Create(new KeyValuePair<string, string?>[]
        {
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("page_size", PageSize.ToString(CultureInfo.InvariantCulture)),
        });

        return (await relay.SendAsync(
            context, HttpMethod.Get, "api/trash/" + query.ToUriComponent(), null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                var results = new JsonArray();
                foreach (var document in (body["results"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var item = new JsonObject();
                    foreach (var field in Fields)
                    {
                        item[field] = document[field]?.DeepClone();
                    }

                    results.Add(item);
                }

                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Json(new JsonObject
                {
                    ["count"] = body["count"]?.DeepClone() ?? 0,
                    ["delai"] = delay,
                    ["results"] = results,
                });
            },
            cancellationToken,
            // Page au-delà de la dernière (corbeille vidée entre-temps) : Paperless répond 404.
            onError: status => status == HttpStatusCode.NotFound
                ? TypedResults.Json(new JsonObject { ["count"] = 0, ["delai"] = delay, ["results"] = new JsonArray() })
                : null))!;
    }

    public sealed record SelectionRequest(JsonNode? Documents, JsonNode? Tout);

    /// <summary>Remet les documents choisis à leur place dans Paperless.</summary>
    private static async Task<IResult> RestoreAsync(
        SelectionRequest? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var (ids, error) = ReadIds(body?.Documents);
        return ids is null
            ? PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide")
            : await SendAsync(context, relay, new JsonObject { ["action"] = "restore", ["documents"] = ids }, cancellationToken);
    }

    /// <summary>
    /// Efface définitivement les documents choisis, ou toute la corbeille visible par l'utilisateur
    /// avec <c>{"tout": true}</c> : jamais par défaut, l'absence de liste n'efface rien.
    /// </summary>
    private static async Task<IResult> EmptyAsync(
        SelectionRequest? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var all = body?.Tout is JsonValue t && t.TryGetValue<bool>(out var value) && value;
        if (all)
        {
            return body!.Documents is null
                ? await SendAsync(context, relay, new JsonObject { ["action"] = "empty" }, cancellationToken)
                : PaperlessRelay.Problem(HttpStatusCode.BadRequest, "Indiquez soit des documents, soit « tout », pas les deux.", "requete_invalide");
        }

        var (ids, error) = ReadIds(body?.Documents);
        return ids is null
            ? PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide")
            : await SendAsync(context, relay, new JsonObject { ["action"] = "empty", ["documents"] = ids }, cancellationToken);
    }

    private static async Task<IResult> SendAsync(
        HttpContext context, PaperlessRelay relay, JsonObject request, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Post, "api/trash/", request,
            (_, _) => Task.FromResult<IResult?>(TypedResults.NoContent()),
            cancellationToken,
            onError: status => status == HttpStatusCode.Forbidden
                ? PaperlessRelay.Problem(
                    HttpStatusCode.Forbidden,
                    "Vous n'avez pas le droit de supprimer tous ces documents : aucun n'a été traité.",
                    "acces_refuse")
                : null))!;

    /// <summary>Liste non vide d'identifiants positifs, sans doublon.</summary>
    internal static (JsonArray? Ids, string? Error) ReadIds(JsonNode? node)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            return (null, "Indiquez au moins un document.");
        }

        if (array.Count > MaxDocuments)
        {
            return (null, $"{MaxDocuments} documents au plus à la fois.");
        }

        var ids = new SortedSet<int>();
        foreach (var item in array)
        {
            if (item is not JsonValue v || !v.TryGetValue<int>(out var id) || id <= 0)
            {
                return (null, "Identifiant de document invalide.");
            }

            ids.Add(id);
        }

        return ([.. ids.Select(id => (JsonNode)id)], null);
    }
}
