using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Hosting;

namespace HomeGed.Api.Documents;

/// <summary>
/// Relais vers Paperless pour la liste des documents, ses filtres (UI-02, UI-03), les actions en
/// masse (UI-06), l'ajout de documents (PWA-07) et la fiche document (UI-04, UI-05). Chaque appel part avec le token de l'utilisateur : Paperless applique
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
            // Permissions : jamais l'e-mail, le statut ni les droits des comptes.
            ["utilisateurs"] = ("users", ["id", "username", "first_name", "last_name"]),
            ["groupes"] = ("groups", ["id", "name"]),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Listes où la fiche peut créer un élément à la volée (UI-05).</summary>
    internal static readonly FrozenSet<string> Creatable = FrozenSet.Create(
        StringComparer.Ordinal, "tags", "correspondants", "types", "chemins");

    public const int MaxVersionLabelLength = 64;

    /// <summary>Algorithme de correspondance « Auto » de Paperless, par défaut à la création (B7, B8).</summary>
    internal const int MatchAuto = 6;

    private const int ReferencePageSize = 500;

    /// <summary>Garde-fou : 20 pages de 500 éléments.</summary>
    private const int MaxReferencePages = 20;

    public static RouteGroupBuilder MapDocuments(this RouteGroupBuilder api)
    {
        api.MapGet("/documents", ListAsync);
        api.MapGet("/documents/ids", IdsAsync);
        api.MapPost("/documents/actions", BulkEditAsync);
        api.MapPost("/documents/telechargement", BulkDownloadAsync);
        api.MapPost("/documents/upload", UploadAsync);
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
        api.MapGet("/documents/{id:int}/history", HistoryAsync);
        api.MapGet("/documents/{id:int}/suggestions", SuggestionsAsync);
        api.MapGet("/documents/{id:int}/permissions", PermissionsAsync);
        api.MapPatch("/documents/{id:int}/permissions", UpdatePermissionsAsync);
        api.MapPost("/documents/{id:int}/versions", UploadVersionAsync);
        api.MapPatch("/documents/{id:int}/versions/{versionId:int}", RenameVersionAsync);
        api.MapDelete("/documents/{id:int}/versions/{versionId:int}", DeleteVersionAsync);
        api.MapGet("/taches/{taskId:guid}", TaskAsync);
        api.MapGet("/references/{kind}", ReferencesAsync);
        api.MapPost("/references/{kind}", CreateReferenceAsync);
        return api;
    }

    private static string Path(int id, string suffix = "") =>
        string.Create(CultureInfo.InvariantCulture, $"api/documents/{id}/{suffix}");

    /// <summary>Plafond des identifiants renvoyés pour précédent / suivant (celui de l'interface Paperless).</summary>
    public const int MaxIds = 100_000;

    /// <summary>Filtres et tri de la liste, en liste blanche ; page et taille sont fixées par l'appelant.</summary>
    private static List<KeyValuePair<string, string?>> ListQuery(HttpContext context, bool withPage)
    {
        var query = new List<KeyValuePair<string, string?>>();
        foreach (var (key, values) in context.Request.Query)
        {
            if (ListParameters.Contains(key) && (withPage || key != "page"))
            {
                query.AddRange(values.Select(v => new KeyValuePair<string, string?>(key, v)));
            }
        }

        return query;
    }

    /// <summary>
    /// Identifiants de tous les documents filtrés, dans l'ordre de la liste (précédent / suivant de la
    /// fiche). L'API v10 de Paperless ne les joint plus aux pages : requête dédiée, champ id seul.
    /// </summary>
    private static async Task<IResult> IdsAsync(HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var query = ListQuery(context, withPage: false);
        query.Add(new("page_size", MaxIds.ToString(CultureInfo.InvariantCulture)));
        query.Add(new("fields", "id"));
        return (await relay.SendAsync(
            context, HttpMethod.Get, "api/documents/" + QueryString.Create(query).ToUriComponent(), null,
            async (response, ct) =>
            {
                var page = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
                var ids = (page?["results"] as JsonArray ?? [])
                    .Select(d => d?["id"] is JsonValue v && v.TryGetValue<int>(out var id) ? id : 0)
                    .Where(id => id > 0);
                return TypedResults.Json(ids);
            },
            cancellationToken,
            isFilter: true))!;
    }

    private static async Task<IResult> ListAsync(HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var query = ListQuery(context, withPage: true);
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

    /// <summary>
    /// Action en masse (UI-06) : Paperless vérifie les droits de l'utilisateur sur chaque document
    /// et refuse l'ensemble si l'un d'eux n'est pas modifiable. Une suppression place les documents
    /// dans la corbeille de Paperless. Réponse 202 avec la tâche quand Paperless l'exécute en fond.
    /// </summary>
    private static async Task<IResult> BulkEditAsync(
        JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var (request, error) = BulkEdit.Sanitize(body);
        if (request is null)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide");
        }

        return (await relay.SendAsync(
            context, HttpMethod.Post, "api/documents/bulk_edit/", request,
            async (response, ct) => BulkTask(await response.Content.ReadAsStringAsync(ct)) is { } task
                ? TypedResults.Json(new { tache = task }, statusCode: StatusCodes.Status202Accepted)
                : TypedResults.NoContent(),
            cancellationToken,
            onError: status => status == HttpStatusCode.Forbidden
                ? PaperlessRelay.Problem(
                    HttpStatusCode.Forbidden,
                    "Vous n'avez pas le droit de faire cette action sur tous les documents sélectionnés : aucun n'a été modifié.",
                    "acces_refuse")
                : null))!;
    }

    /// <summary>
    /// Paperless 3.5 exécute l'action en tâche de fond : identifiant de la tâche (chaîne seule ou
    /// champ <c>task_id</c>), pour que l'écran attende la fin avant de recharger la liste.
    /// </summary>
    internal static Guid? BulkTask(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var id = node is JsonObject obj ? obj["task_id"] : node;
            return id is JsonValue v && v.TryGetValue<string>(out var text) && Guid.TryParse(text, out var task) ? task : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Archive zip des documents sélectionnés, relayée en flux (UI-06).</summary>
    private static async Task<IResult> BulkDownloadAsync(
        JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var (request, error) = BulkEdit.SanitizeDownload(body);
        return request is null
            ? PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide")
            : await StreamAsync(
                context, relay, "api/documents/bulk_download/", "private, no-store", framable: false, cancellationToken,
                HttpMethod.Post, request);
    }

    private static Task<IResult> ThumbnailAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        StreamAsync(context, relay, Path(id, "thumb/"), "private, max-age=600", framable: false, cancellationToken);

    /// <summary>
    /// Aperçu affiché dans un cadre de la fiche : seule réponse que Home-GED accepte d'afficher dans
    /// un cadre, et seulement depuis ses propres pages.
    /// </summary>
    private static Task<IResult> PreviewAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        StreamAsync(context, relay, Path(id, "preview/") + FileQuery(context), "private, no-cache", framable: true, cancellationToken);

    private static Task<IResult> DownloadAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        StreamAsync(context, relay, Path(id, "download/") + FileQuery(context), "private, no-cache", framable: false, cancellationToken);

    /// <summary>
    /// Fichier demandé : original plutôt que l'archive PDF/A produite par l'OCR, et version précise
    /// du document (versions Paperless 3) ; seuls ces deux paramètres, validés, sont transmis.
    /// </summary>
    internal static string FileQuery(HttpContext context)
    {
        var query = new List<KeyValuePair<string, string?>>();
        if (context.Request.Query["original"] == "true")
        {
            query.Add(new("original", "true"));
        }

        if (int.TryParse(context.Request.Query["version"], NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0)
        {
            query.Add(new("version", version.ToString(CultureInfo.InvariantCulture)));
        }

        return QueryString.Create(query).ToUriComponent();
    }

    private static async Task<IResult> StreamAsync(
        HttpContext context,
        PaperlessRelay relay,
        string uri,
        string cacheControl,
        bool framable,
        CancellationToken cancellationToken,
        HttpMethod? method = null,
        JsonObject? body = null) =>
        (await relay.SendAsync(
            context, method ?? HttpMethod.Get, uri, body,
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

    /// <summary>
    /// Historique (journal d'audit) : Paperless le réserve aux comptes qui ont le droit de voir le
    /// journal, pour leurs documents ou ceux sans propriétaire.
    /// </summary>
    /// <summary>Listes de suggestions de Paperless renvoyées par <c>GET /documents/{id}/suggestions</c> (UI-12).</summary>
    internal static readonly string[] SuggestionLists = ["tags", "correspondents", "document_types", "storage_paths"];

    /// <summary>
    /// Suggestions de classement calculées par Paperless (algorithmes de correspondance et modèle
    /// entraîné) : identifiants de tags, correspondants, types et chemins, et dates trouvées dans le
    /// contenu. Seuls ces champs, validés, sont renvoyés.
    /// </summary>
    private static async Task<IResult> SuggestionsAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Get, Path(id, "suggestions/"), null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                var suggestions = new JsonObject();
                foreach (var list in SuggestionLists)
                {
                    suggestions[list] = new JsonArray([.. (body[list] as JsonArray ?? [])
                        .Select(n => n is JsonValue v && v.TryGetValue<int>(out var i) && i > 0 ? i : 0)
                        .Where(i => i > 0)
                        .Distinct()
                        .Select(i => (JsonNode)i)]);
                }

                suggestions["dates"] = new JsonArray([.. (body["dates"] as JsonArray ?? [])
                    .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) && s.Length >= 10
                        && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : null)
                    .OfType<string>()
                    .Distinct()
                    .Select(s => (JsonNode)s)]);

                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Json(suggestions);
            },
            cancellationToken))!;

    private static async Task<IResult> HistoryAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Get, Path(id, "history/"), null, PaperlessRelay.JsonBody, cancellationToken,
            onError: status => status switch
            {
                HttpStatusCode.Forbidden => PaperlessRelay.Problem(
                    HttpStatusCode.Forbidden,
                    "L'historique est réservé au propriétaire du document qui a le droit de consulter le journal d'audit, et aux administrateurs.",
                    "historique_reserve"),
                HttpStatusCode.BadRequest => PaperlessRelay.Problem(
                    HttpStatusCode.BadRequest, "Le journal d'audit est désactivé dans Paperless.", "historique_desactive"),
                _ => null,
            }))!;

    private static Task<IResult> PermissionsAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        PermissionsResultAsync(context, relay, HttpMethod.Get, id, null, cancellationToken);

    /// <summary>
    /// Propriétaire et droits du document. Paperless n'accepte la modification que du propriétaire,
    /// d'un administrateur, ou pour un document sans propriétaire.
    /// </summary>
    private static async Task<IResult> UpdatePermissionsAsync(
        int id, JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var (update, error) = DocumentUpdate.SanitizePermissions(body);
        return update is null
            ? PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide")
            : await PermissionsResultAsync(context, relay, HttpMethod.Patch, id, update, cancellationToken);
    }

    private static async Task<IResult> PermissionsResultAsync(
        HttpContext context, PaperlessRelay relay, HttpMethod method, int id, JsonObject? body, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, method, Path(id) + "?full_perms=true", body,
            async (response, ct) =>
            {
                var document = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                return TypedResults.Json(Project(document, ["owner", "permissions"]));
            },
            cancellationToken,
            onError: status => status == HttpStatusCode.Forbidden && method == HttpMethod.Patch
                ? PaperlessRelay.Problem(
                    HttpStatusCode.Forbidden,
                    "Seul le propriétaire du document (ou un administrateur) peut modifier ses permissions.",
                    "acces_refuse")
                : null))!;

    /// <summary>
    /// Nouvelle version du document : le multipart du navigateur (champ <c>document</c>, libellé
    /// facultatif <c>version_label</c>) est relayé en flux. Paperless la traite en tâche de fond (OCR).
    /// </summary>
    private static Task<IResult> UploadVersionAsync(
        int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        RelayUploadAsync(
            context, relay, Path(id, "update_version/"), "Vous ne pouvez pas modifier ce document.", cancellationToken);

    /// <summary>
    /// Ajout d'un document (PWA-07) : le multipart du navigateur (champ <c>document</c>) est relayé en
    /// flux vers <c>post_document</c>. Paperless le consomme en tâche de fond (OCR, classement) ; le
    /// document appartient à l'utilisateur, avec ses permissions par défaut.
    /// </summary>
    private static Task<IResult> UploadAsync(HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        RelayUploadAsync(
            context, relay, "api/documents/post_document/", "Votre compte n'a pas le droit d'ajouter des documents.", cancellationToken);

    /// <summary>
    /// Relais d'un envoi de fichier : multipart vérifié (type, taille annoncée, 200 Mo au plus) puis
    /// transmis au fil de sa réception. Rend 202 et l'identifiant de la tâche Paperless.
    /// </summary>
    private static async Task<IResult> RelayUploadAsync(
        HttpContext context, PaperlessRelay relay, string uri, string forbidden, CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            || type.MediaType != "multipart/form-data"
            || !type.Parameters.Any(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase)))
        {
            return PaperlessRelay.Problem(HttpStatusCode.UnsupportedMediaType, "Envoi de fichier attendu.", "requete_invalide");
        }

        if (request.ContentLength is not { } length || length == 0)
        {
            return PaperlessRelay.Problem(HttpStatusCode.LengthRequired, "Taille du fichier inconnue.", "requete_invalide");
        }

        if (length > HomeGedApp.MaxRequestBodyBytes)
        {
            return PaperlessRelay.Problem(HttpStatusCode.RequestEntityTooLarge, "Fichier trop volumineux (200 Mo au plus).", "fichier_trop_gros");
        }

        using var content = new StreamContent(request.Body);
        content.Headers.ContentType = type;
        content.Headers.ContentLength = length;
        return (await relay.SendAsync(
            context, HttpMethod.Post, uri, null,
            async (response, ct) =>
            {
                var task = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
                return task is JsonValue value && value.TryGetValue<string>(out var taskId) && Guid.TryParse(taskId, out var guid)
                    ? TypedResults.Json(new { tache = guid }, statusCode: StatusCodes.Status202Accepted)
                    : throw new HttpRequestException("Réponse de Paperless sans tâche.");
            },
            cancellationToken,
            onError: status => status == HttpStatusCode.Forbidden
                ? PaperlessRelay.Problem(HttpStatusCode.Forbidden, forbidden, "acces_refuse")
                : null,
            upload: content))!;
    }

    public sealed record VersionLabelRequest(string? VersionLabel);

    private static async Task<IResult> RenameVersionAsync(
        int id, int versionId, VersionLabelRequest body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var label = body.VersionLabel?.Trim() ?? "";
        if (label.Length > MaxVersionLabelLength)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, "Libellé trop long (64 caractères au plus).", "requete_invalide");
        }

        return (await relay.SendAsync(
            context, HttpMethod.Patch, Path(id, string.Create(CultureInfo.InvariantCulture, $"versions/{versionId}/")),
            new JsonObject { ["version_label"] = label.Length == 0 ? null : label },
            PaperlessRelay.JsonBody, cancellationToken))!;
    }

    /// <summary>Supprime une version ; Paperless refuse de supprimer la dernière version restante.</summary>
    private static async Task<IResult> DeleteVersionAsync(
        int id, int versionId, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Delete, Path(id, string.Create(CultureInfo.InvariantCulture, $"versions/{versionId}/")), null,
            (_, _) => Task.FromResult<IResult?>(TypedResults.NoContent()),
            cancellationToken))!;

    /// <summary>
    /// État d'une tâche de traitement Paperless (OCR d'un envoi), limité à ce que l'écran affiche :
    /// statut (pending, started, success, failure, revoked), message d'échec, document produit.
    /// </summary>
    private static async Task<IResult> TaskAsync(Guid taskId, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Get, string.Create(CultureInfo.InvariantCulture, $"api/tasks/?task_id={taskId:D}"), null,
            async (response, ct) =>
            {
                var page = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
                var task = (page?["results"] as JsonArray ?? page as JsonArray)?.OfType<JsonObject>()
                    .FirstOrDefault(t => t["task_id"]?.GetValue<string>() == taskId.ToString("D"));
                return TypedResults.Json(TaskState(task));
            },
            cancellationToken))!;

    internal static JsonObject TaskState(JsonObject? task)
    {
        if (task is null)
        {
            // Pas encore enregistrée par Paperless : elle attend son tour.
            return new JsonObject { ["status"] = "pending", ["message"] = null, ["document_id"] = null };
        }

        var data = task["result_data"] as JsonObject;
        var message = Text(data?["reason"]) ?? Text(data?["error_message"]);
        var document = Integer(data?["document_id"])
            ?? (task["related_document_ids"] as JsonArray)?.Select(Integer).FirstOrDefault(id => id is not null);
        return new JsonObject
        {
            ["status"] = Text(task["status"])?.ToLowerInvariant() ?? "pending",
            ["message"] = message is { Length: > 500 } ? message[..500] : message,
            ["document_id"] = document,
        };

        static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

        static int? Integer(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var id) ? id : null;
    }

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
        if (!References.TryGetValue(kind, out var reference) || !Creatable.Contains(kind))
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
