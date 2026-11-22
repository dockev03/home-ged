using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Hosting;

namespace HomeGed.Api.Documents;

/// <summary>
/// Relais vers Paperless pour la liste des documents, ses filtres (UI-02, UI-03) et la fiche
/// document (UI-04, UI-05). Chaque appel part avec le token de l'utilisateur : Paperless applique
/// ses droits. Seuls les paramètres et les champs connus sont transmis.
/// </summary>
public static class DocumentEndpoints
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    public const int MaxNoteLength = 10_000;

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
            ["champs"] = ("custom_fields", ["id", "name", "data_type", "extra_data"]),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Algorithme de correspondance « Auto » de Paperless, par défaut à la création (B7, B8).</summary>
    internal const int MatchAuto = 6;

    private const int ReferencePageSize = 500;

    /// <summary>Garde-fou : 20 pages de 500 éléments.</summary>
    private const int MaxReferencePages = 20;

    public static RouteGroupBuilder MapDocuments(this RouteGroupBuilder api)
    {
        api.MapGet("/documents", ListAsync);
        api.MapGet("/documents/{id:int}", GetAsync);
        api.MapPatch("/documents/{id:int}", UpdateAsync);
        api.MapDelete("/documents/{id:int}", DeleteAsync);
        api.MapGet("/documents/{id:int}/thumb", ThumbnailAsync);
        api.MapGet("/documents/{id:int}/preview", PreviewAsync);
        api.MapGet("/documents/{id:int}/download", DownloadAsync);
        api.MapGet("/documents/{id:int}/metadata", MetadataAsync);
        api.MapGet("/documents/{id:int}/notes", NotesAsync);
        api.MapPost("/documents/{id:int}/notes", AddNoteAsync);
        api.MapDelete("/documents/{id:int}/notes/{noteId:int}", DeleteNoteAsync);
        api.MapGet("/references/{kind}", ReferencesAsync);
        api.MapPost("/references/{kind}", CreateReferenceAsync);
        return api;
    }

    private static string Path(int id, string suffix = "") =>
        string.Create(CultureInfo.InvariantCulture, $"api/documents/{id}/{suffix}");

    private static async Task<IResult> ListAsync(HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
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

        return (await relay.SendAsync(
            context, HttpMethod.Get, "api/documents/" + QueryString.Create(query).ToUriComponent(), null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                // Liens de pagination : ils contiennent l'adresse interne de Paperless.
                body.Remove("next");
                body.Remove("previous");
                return TypedResults.Json(body);
            },
            cancellationToken,
            isFilter: true))!;
    }

    private static async Task<IResult> GetAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(context, HttpMethod.Get, Path(id), null, PaperlessRelay.JsonBody, cancellationToken))!;

    private static async Task<IResult> UpdateAsync(
        int id, JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var (update, error) = DocumentUpdate.Sanitize(body);
        if (update is null)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide");
        }

        return (await relay.SendAsync(context, HttpMethod.Patch, Path(id), update, PaperlessRelay.JsonBody, cancellationToken))!;
    }

    /// <summary>Paperless place le document dans sa corbeille (restaurable).</summary>
    private static async Task<IResult> DeleteAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Delete, Path(id), null,
            (_, _) => Task.FromResult<IResult?>(TypedResults.NoContent()),
            cancellationToken))!;

    private static Task<IResult> ThumbnailAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        StreamAsync(context, relay, Path(id, "thumb/"), "private, max-age=600", framable: false, cancellationToken);

    /// <summary>
    /// Aperçu affiché dans un cadre de la fiche : seule réponse que Home-GED accepte d'afficher dans
    /// un cadre, et seulement depuis ses propres pages.
    /// </summary>
    private static Task<IResult> PreviewAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        StreamAsync(context, relay, Path(id, "preview/") + OriginalQuery(context), "private, no-cache", framable: true, cancellationToken);

    private static Task<IResult> DownloadAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        StreamAsync(context, relay, Path(id, "download/") + OriginalQuery(context), "private, no-cache", framable: false, cancellationToken);

    /// <summary>Fichier original plutôt que la version archivée (PDF/A) produite par l'OCR.</summary>
    private static string OriginalQuery(HttpContext context) =>
        context.Request.Query["original"] == "true" ? "?original=true" : "";

    private static async Task<IResult> StreamAsync(
        HttpContext context, PaperlessRelay relay, string uri, string cacheControl, bool framable, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Get, uri, null,
            async (response, ct) =>
            {
                var content = response.Content.Headers;
                var stream = await response.Content.ReadAsStreamAsync(ct);
                var headers = context.Response.Headers;
                headers.CacheControl = cacheControl;
                if (content.ContentDisposition is { } disposition)
                {
                    headers.ContentDisposition = disposition.ToString();
                }

                if (content.ContentLength is { } length)
                {
                    context.Response.ContentLength = length;
                }

                var type = content.ContentType?.MediaType ?? "application/octet-stream";
                if (framable && IsInertPreviewType(type))
                {
                    SecurityHeaders.AllowSameOriginFraming(context.Response);
                }
                else if (framable)
                {
                    // Type inattendu (HTML…) : jamais exécuté dans l'origine de Home-GED.
                    SecurityHeaders.Sandbox(context.Response);
                }

                return TypedResults.Stream(stream, type);
            },
            cancellationToken,
            keepResponse: true))!;

    /// <summary>Types que le navigateur affiche sans exécuter de code : seuls admis dans le cadre d'aperçu.</summary>
    internal static bool IsInertPreviewType(string mediaType) =>
        mediaType is "application/pdf" or "text/plain"
        || (mediaType.StartsWith("image/", StringComparison.Ordinal) && mediaType != "image/svg+xml");

    private static async Task<IResult> MetadataAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(context, HttpMethod.Get, Path(id, "metadata/"), null, PaperlessRelay.JsonBody, cancellationToken))!;

    private static async Task<IResult> NotesAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(context, HttpMethod.Get, Path(id, "notes/"), null, PaperlessRelay.JsonBody, cancellationToken))!;

    public sealed record NoteRequest(string? Note);

    private static async Task<IResult> AddNoteAsync(
        int id, NoteRequest body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var note = body.Note?.Trim() ?? "";
        if (note.Length is 0 or > MaxNoteLength)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, "Note vide ou trop longue.", "requete_invalide");
        }

        return (await relay.SendAsync(
            context, HttpMethod.Post, Path(id, "notes/"), new JsonObject { ["note"] = note }, PaperlessRelay.JsonBody, cancellationToken))!;
    }

    private static async Task<IResult> DeleteNoteAsync(
        int id, int noteId, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Delete,
            Path(id, "notes/") + string.Create(CultureInfo.InvariantCulture, $"?id={noteId}"), null,
            PaperlessRelay.JsonBody, cancellationToken))!;

    private static async Task<IResult> ReferencesAsync(
        string kind, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        if (!References.TryGetValue(kind, out var reference))
        {
            return PaperlessRelay.Problem(HttpStatusCode.NotFound, "Liste inconnue.", "liste_inconnue");
        }

        var items = new JsonArray();
        for (var page = 1; page <= MaxReferencePages; page++)
        {
            var uri = string.Create(
                CultureInfo.InvariantCulture,
                $"api/{reference.Resource}/?page={page}&page_size={ReferencePageSize}&ordering=name");
            var hasNext = false;
            var result = await relay.SendAsync(
                context, HttpMethod.Get, uri, null,
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

    public sealed record CreateReferenceRequest(string? Name, string? Color, string? Path);

    /// <summary>
    /// Création à la volée depuis la fiche (UI-05) : classement « Auto », et les permissions par défaut
    /// que l'utilisateur a réglées dans Paperless, comme le fait l'interface Paperless.
    /// </summary>
    private static async Task<IResult> CreateReferenceAsync(
        string kind, CreateReferenceRequest body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        if (!References.TryGetValue(kind, out var reference) || kind == "champs")
        {
            return PaperlessRelay.Problem(HttpStatusCode.NotFound, "Liste inconnue.", "liste_inconnue");
        }

        var name = body.Name?.Trim() ?? "";
        var color = body.Color?.Trim();
        var path = body.Path?.Trim() ?? "";
        if (name.Length is 0 or > 128
            || (kind == "tags" && color is not null && !IsHexColor(color))
            || (kind == "chemins" && path.Length is 0 or > 2048))
        {
            return PaperlessRelay.Problem(
                HttpStatusCode.BadRequest,
                kind == "chemins" ? "Nom et chemin obligatoires." : "Nom obligatoire (128 caractères au plus).",
                "requete_invalide");
        }

        var created = new JsonObject { ["name"] = name, ["matching_algorithm"] = MatchAuto };
        if (kind == "tags" && color is not null)
        {
            created["color"] = color;
        }

        if (kind == "chemins")
        {
            created["path"] = path;
        }

        var defaults = await relay.SendAsync(
            context, HttpMethod.Get, "api/ui_settings/", null,
            async (response, ct) =>
            {
                ApplyDefaultPermissions(await response.Content.ReadFromJsonAsync<JsonObject>(ct), created);
                return null;
            },
            cancellationToken);
        if (defaults is not null)
        {
            return defaults;
        }

        return (await relay.SendAsync(
            context, HttpMethod.Post, $"api/{reference.Resource}/", created,
            async (response, ct) =>
            {
                var item = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                return TypedResults.Json(Project(item, reference.Fields), statusCode: StatusCodes.Status201Created);
            },
            cancellationToken))!;
    }

    /// <summary>
    /// Réglages « permissions par défaut » de l'utilisateur (<c>settings.permissions</c> de ui_settings) :
    /// propriétaire (absent = l'utilisateur lui-même) et droits de lecture et de modification.
    /// </summary>
    internal static void ApplyDefaultPermissions(JsonObject? uiSettings, JsonObject target)
    {
        if (uiSettings?["settings"]?["permissions"] is not JsonObject permissions)
        {
            return;
        }

        if (permissions.TryGetPropertyValue("default_owner", out var owner)
            && (owner is null || (owner is JsonValue v && v.TryGetValue<int>(out _))))
        {
            target["owner"] = owner?.DeepClone();
        }

        JsonArray Ids(string key) =>
            new([.. (permissions[key] as JsonArray ?? [])
                .Select(n => n is JsonValue value && value.TryGetValue<int>(out var id) ? id : (int?)null)
                .Where(id => id is not null)
                .Select(id => (JsonNode)JsonValue.Create(id!.Value))]);

        target["set_permissions"] = new JsonObject
        {
            ["view"] = new JsonObject { ["users"] = Ids("default_view_users"), ["groups"] = Ids("default_view_groups") },
            ["change"] = new JsonObject { ["users"] = Ids("default_edit_users"), ["groups"] = Ids("default_edit_groups") },
        };
    }

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);

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
}
