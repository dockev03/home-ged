using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace HomeGed.Api.Documents;

/// <summary>
/// Écrans « Gérer » (UI-07) : liste paginée, création, modification et suppression des étiquettes,
/// correspondants, types de documents, chemins de stockage et champs personnalisés. Chaque appel
/// part avec le token de l'utilisateur : Paperless applique ses droits (permissions globales et
/// propriétaire de l'objet).
/// </summary>
public static class ManageEndpoints
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    public const int MaxSearchLength = 128;

    private static readonly string[] Common = ["id", "name", "document_count"];

    private static readonly string[] Matching = ["match", "matching_algorithm", "is_insensitive"];

    private static readonly string[] Ownership = ["owner", "permissions", "user_can_change"];

    /// <summary>Listes gérables : ressource Paperless et champs renvoyés au navigateur.</summary>
    internal static readonly FrozenDictionary<string, (string Resource, string[] Fields)> Kinds =
        new Dictionary<string, (string, string[])>(StringComparer.Ordinal)
        {
            ["tags"] = ("tags", [.. Common, .. Matching, .. Ownership, "color", "text_color", "is_inbox_tag"]),
            ["correspondants"] = ("correspondents", [.. Common, .. Matching, .. Ownership, "last_correspondence"]),
            ["types"] = ("document_types", [.. Common, .. Matching, .. Ownership]),
            ["chemins"] = ("storage_paths", [.. Common, .. Matching, .. Ownership, "path"]),
            // Les champs personnalisés n'ont ni propriétaire ni correspondance dans Paperless.
            ["champs"] = ("custom_fields", [.. Common, "data_type", "extra_data"]),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Tris proposés (et leur inverse, préfixé par « - »).</summary>
    internal static readonly FrozenSet<string> Orderings = FrozenSet.Create(
        StringComparer.Ordinal, "name", "document_count", "matching_algorithm");

    public static RouteGroupBuilder MapManagement(this RouteGroupBuilder api)
    {
        api.MapGet("/gestion/{kind}", ListAsync);
        api.MapPost("/gestion/{kind}", CreateAsync);
        api.MapPatch("/gestion/{kind}/{id:int}", UpdateAsync);
        api.MapDelete("/gestion/{kind}/{id:int}", DeleteAsync);
        return api;
    }

    private static string ItemPath(string resource, int id) =>
        string.Create(CultureInfo.InvariantCulture, $"api/{resource}/{id}/");

    private static IResult UnknownKind() =>
        PaperlessRelay.Problem(HttpStatusCode.NotFound, "Liste inconnue.", "liste_inconnue");

    /// <summary>Traduction des refus propres aux objets gérés (le relais parle de documents).</summary>
    private static IResult? OnError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => PaperlessRelay.Problem(
            HttpStatusCode.Forbidden, "Vous n'avez pas le droit de faire cette modification dans Paperless.", "acces_refuse"),
        HttpStatusCode.NotFound => PaperlessRelay.Problem(HttpStatusCode.NotFound, "Élément introuvable.", "introuvable"),
        _ => null,
    };

    /// <summary>
    /// Page de la liste : recherche dans le nom (<c>nom</c>), tri en liste blanche (<c>tri</c>),
    /// permissions complètes pour l'écran de modification.
    /// </summary>
    private static async Task<IResult> ListAsync(
        string kind, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        if (!Kinds.TryGetValue(kind, out var managed))
        {
            return UnknownKind();
        }

        var request = context.Request.Query;
        var query = new List<KeyValuePair<string, string?>>
        {
            new("page", Page(request["page"]).ToString(CultureInfo.InvariantCulture)),
            new("page_size", PageSize(request["page_size"]).ToString(CultureInfo.InvariantCulture)),
            new("ordering", Ordering(request["tri"])),
        };
        if (request["nom"].ToString().Trim() is { Length: > 0 } search)
        {
            query.Add(new("name__icontains", search.Length > MaxSearchLength ? search[..MaxSearchLength] : search));
        }

        if (kind != "champs")
        {
            query.Add(new("full_perms", "true"));
        }

        return (await relay.SendAsync(
            context, HttpMethod.Get, $"api/{managed.Resource}/" + QueryString.Create(query).ToUriComponent(), null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                var results = new JsonArray();
                foreach (var item in body["results"] as JsonArray ?? [])
                {
                    if (item is JsonObject source)
                    {
                        results.Add(Project(source, managed.Fields));
                    }
                }

                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Json(new JsonObject { ["count"] = body["count"]?.DeepClone() ?? 0, ["results"] = results });
            },
            cancellationToken,
            onError: OnError))!;
    }

    /// <summary>
    /// Création : sans permissions explicites, celles par défaut que l'utilisateur a réglées dans
    /// Paperless, comme le fait l'interface Paperless ; classement « Auto » par défaut.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        string kind, JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        if (!Kinds.TryGetValue(kind, out var managed))
        {
            return UnknownKind();
        }

        var (created, error) = ManagedObject.Sanitize(kind, body, creating: true);
        if (created is null)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide");
        }

        if (kind != "champs")
        {
            if (!created.ContainsKey("matching_algorithm"))
            {
                created["matching_algorithm"] = DocumentEndpoints.MatchAuto;
            }

            if (!created.ContainsKey("set_permissions"))
            {
                var defaults = await relay.SendAsync(
                    context, HttpMethod.Get, "api/ui_settings/", null,
                    async (response, ct) =>
                    {
                        DocumentEndpoints.ApplyDefaultPermissions(await response.Content.ReadFromJsonAsync<JsonObject>(ct), created);
                        return null;
                    },
                    cancellationToken);
                if (defaults is not null)
                {
                    return defaults;
                }
            }
        }

        return (await relay.SendAsync(
            context, HttpMethod.Post, $"api/{managed.Resource}/", created,
            async (response, ct) => TypedResults.Json(
                await ProjectAsync(response, managed.Fields, ct), statusCode: StatusCodes.Status201Created),
            cancellationToken,
            onError: OnError))!;
    }

    private static async Task<IResult> UpdateAsync(
        string kind, int id, JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        if (!Kinds.TryGetValue(kind, out var managed))
        {
            return UnknownKind();
        }

        var (update, error) = ManagedObject.Sanitize(kind, body, creating: false);
        if (update is null)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide");
        }

        var uri = ItemPath(managed.Resource, id) + (kind == "champs" ? "" : "?full_perms=true");
        return (await relay.SendAsync(
            context, HttpMethod.Patch, uri, update,
            async (response, ct) => TypedResults.Json(await ProjectAsync(response, managed.Fields, ct)),
            cancellationToken,
            onError: OnError))!;
    }

    /// <summary>
    /// Suppression définitive de l'objet. Paperless le retire des documents (étiquette, correspondant…)
    /// ou efface ses valeurs (champ personnalisé) ; aucun document n'est supprimé.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        string kind, int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        !Kinds.TryGetValue(kind, out var managed)
            ? UnknownKind()
            : (await relay.SendAsync(
                context, HttpMethod.Delete, ItemPath(managed.Resource, id), null,
                (_, _) => Task.FromResult<IResult?>(TypedResults.NoContent()),
                cancellationToken,
                onError: OnError))!;

    internal static int Page(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page >= 1 ? Math.Min(page, 10_000) : 1;

    internal static int PageSize(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            ? Math.Clamp(size, 1, MaxPageSize)
            : DefaultPageSize;

    /// <summary>Tri demandé s'il est autorisé, sinon par nom.</summary>
    internal static string Ordering(string? value)
    {
        var field = value?.StartsWith('-') == true ? value[1..] : value;
        return field is not null && Orderings.Contains(field) ? value! : "name";
    }

    private static async Task<JsonObject> ProjectAsync(HttpResponseMessage response, string[] fields, CancellationToken cancellationToken)
    {
        var item = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
            ?? throw new HttpRequestException("Réponse de Paperless vide.");
        return Project(item, fields);
    }

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
