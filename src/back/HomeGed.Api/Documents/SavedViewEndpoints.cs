using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeGed.Api.Documents;

/// <summary>
/// Vues sauvegardées (UI-08), stockées dans Paperless : mêmes vues que dans son interface, droits
/// appliqués par Paperless. L'affichage sur le tableau de bord et dans le menu est une préférence
/// de l'utilisateur, rangée comme le fait l'interface Paperless (API v10) dans
/// <c>ui_settings.settings.saved_views</c>.
/// </summary>
public static class SavedViewEndpoints
{
    public const int MaxRules = 100;

    public const int MaxRuleValueLength = 1024;

    public const int MaxSortFieldLength = 64;

    /// <summary>Dernier type de règle de filtre connu de Paperless 3.5 (« has duplicates »).</summary>
    public const int MaxRuleType = 50;

    /// <summary>Garde-fou : une seule page de vues, largement au-delà d'un usage familial.</summary>
    internal const int PageSize = 1000;

    private static readonly string[] Fields = ["id", "name", "filter_rules", "sort_field", "sort_reverse", "owner", "user_can_change"];

    /// <summary>
    /// Clés que Paperless ajoute à la lecture de ui_settings (configuration du serveur, liens OAuth) :
    /// elles ne sont pas des préférences et ne sont pas renvoyées à l'enregistrement.
    /// </summary>
    private static readonly string[] ServerSettings =
    [
        "trash_delay", "version", "app_title", "app_logo", "auditlog_enabled", "remote_ocr",
        "gmail_oauth_url", "outlook_oauth_url", "email_enabled", "ai_enabled",
    ];

    internal const string DashboardIds = "dashboard_views_visible_ids";
    internal const string SidebarIds = "sidebar_views_visible_ids";
    internal const string DashboardOrder = "dashboard_views_sort_order";
    internal const string SidebarOrder = "sidebar_views_sort_order";

    public static RouteGroupBuilder MapSavedViews(this RouteGroupBuilder api)
    {
        api.MapGet("/vues", ListAsync);
        api.MapPost("/vues", CreateAsync);
        api.MapPatch("/vues/{id:int}", UpdateAsync);
        api.MapDelete("/vues/{id:int}", DeleteAsync);
        return api;
    }

    private static string ItemPath(int id) => string.Create(CultureInfo.InvariantCulture, $"api/saved_views/{id}/");

    private static IResult? OnError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => PaperlessRelay.Problem(
            HttpStatusCode.Forbidden, "Vous n'avez pas le droit de modifier cette vue dans Paperless.", "acces_refuse"),
        HttpStatusCode.NotFound => PaperlessRelay.Problem(HttpStatusCode.NotFound, "Vue introuvable.", "introuvable"),
        _ => null,
    };

    /// <summary>
    /// Vues visibles par l'utilisateur, avec les identifiants affichés sur le tableau de bord et dans
    /// le menu, dans l'ordre choisi dans Paperless (les vues sans ordre à la suite).
    /// </summary>
    private static async Task<IResult> ListAsync(HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        JsonArray? views = null;
        var listed = await relay.SendAsync(
            context, HttpMethod.Get,
            string.Create(CultureInfo.InvariantCulture, $"api/saved_views/?page_size={PageSize}&ordering=name"), null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                views = [.. (body["results"] as JsonArray ?? []).OfType<JsonObject>().Select(Project)];
                return null;
            },
            cancellationToken,
            onError: OnError);
        if (listed is not null)
        {
            return listed;
        }

        JsonObject? saved = null;
        var read = await ReadSettingsAsync(context, relay, s => saved = s["saved_views"] as JsonObject, cancellationToken);
        if (read is not null)
        {
            return read;
        }

        var existing = views!.Select(v => v!["id"]!.GetValue<int>()).ToHashSet();
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Json(new JsonObject
        {
            ["vues"] = views,
            ["tableau_de_bord"] = Ordered(saved, DashboardIds, DashboardOrder, existing),
            ["menu"] = Ordered(saved, SidebarIds, SidebarOrder, existing),
        });
    }

    private static async Task<IResult> CreateAsync(
        JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var (view, visibility, error) = Sanitize(body, creating: true);
        if (view is null)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide");
        }

        JsonObject? created = null;
        var result = await relay.SendAsync(
            context, HttpMethod.Post, "api/saved_views/", view,
            async (response, ct) =>
            {
                created = Project(await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide."));
                return null;
            },
            cancellationToken,
            onError: OnError);
        if (result is not null)
        {
            return result;
        }

        return await UpdateVisibilityAsync(context, relay, created!["id"]!.GetValue<int>(), visibility, cancellationToken)
            ?? TypedResults.Json(created, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Modification : critères, tri et nom dans Paperless (seulement s'ils sont envoyés), puis
    /// affichage sur le tableau de bord et dans le menu (préférence de l'utilisateur).
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        int id, JsonNode? body, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken)
    {
        var (view, visibility, error) = Sanitize(body, creating: false);
        if (view is null)
        {
            return PaperlessRelay.Problem(HttpStatusCode.BadRequest, error!, "requete_invalide");
        }

        // PATCH vide : lecture de la vue, qui vérifie qu'elle existe et reste visible pour l'utilisateur.
        JsonObject? updated = null;
        var result = await relay.SendAsync(
            context, view.Count > 0 ? HttpMethod.Patch : HttpMethod.Get, ItemPath(id), view.Count > 0 ? view : null,
            async (response, ct) =>
            {
                updated = Project(await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide."));
                return null;
            },
            cancellationToken,
            onError: OnError);
        if (result is not null)
        {
            return result;
        }

        return await UpdateVisibilityAsync(context, relay, id, visibility, cancellationToken)
            ?? TypedResults.Json(updated);
    }

    /// <summary>Suppression dans Paperless, pour tous les utilisateurs qui y ont accès.</summary>
    private static async Task<IResult> DeleteAsync(int id, HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Delete, ItemPath(id), null,
            (_, _) => Task.FromResult<IResult?>(TypedResults.NoContent()),
            cancellationToken,
            onError: OnError))!;

    /// <summary>
    /// Corps à transmettre à Paperless (nom, règles, tri) et affichage demandé, ou le message d'erreur.
    /// Les règles sont vérifiées dans leur forme (type connu, valeur texte bornée) ; leur sens reste à Paperless.
    /// </summary>
    internal static (JsonObject? View, (bool? Dashboard, bool? Sidebar) Visibility, string? Error) Sanitize(JsonNode? body, bool creating)
    {
        if (body is not JsonObject source || source.Count == 0)
        {
            return (null, default, "Aucune modification.");
        }

        var view = new JsonObject();
        bool? dashboard = null;
        bool? sidebar = null;
        foreach (var (key, value) in source)
        {
            var (clean, valid) = key switch
            {
                "name" => ManagedObject.Text(value, 1, ManagedObject.MaxNameLength),
                "filter_rules" => Rules(value),
                "sort_field" => SortField(value),
                "sort_reverse" or "tableau_de_bord" or "menu" => ManagedObject.Boolean(value),
                _ => (null, false),
            };
            if (!valid)
            {
                return (null, default, string.Create(CultureInfo.InvariantCulture, $"Champ « {key} » invalide ou non modifiable."));
            }

            switch (key)
            {
                case "tableau_de_bord":
                    dashboard = clean!.GetValue<bool>();
                    break;
                case "menu":
                    sidebar = clean!.GetValue<bool>();
                    break;
                default:
                    view[key] = clean;
                    break;
            }
        }

        if (creating)
        {
            if (!view.ContainsKey("name"))
            {
                return (null, default, "Nom obligatoire.");
            }

            if (!view.ContainsKey("filter_rules"))
            {
                view["filter_rules"] = new JsonArray();
            }
        }

        return (view, (dashboard, sidebar), null);
    }

    private static (JsonNode?, bool) Rules(JsonNode? value)
    {
        if (value is not JsonArray array || array.Count > MaxRules)
        {
            return (null, false);
        }

        var rules = new JsonArray();
        foreach (var item in array)
        {
            if (item is not JsonObject rule || rule.Any(p => p.Key is not ("rule_type" or "value"))
                || ManagedObject.Integer(rule["rule_type"], 0, MaxRuleType) is not ({ } type, true))
            {
                return (null, false);
            }

            JsonNode? ruleValue = null;
            if (rule["value"] is { } node)
            {
                if (ManagedObject.Text(node, 0, MaxRuleValueLength, trim: false) is not ({ } text, true))
                {
                    return (null, false);
                }

                ruleValue = text;
            }

            rules.Add(new JsonObject { ["rule_type"] = type, ["value"] = ruleValue });
        }

        return (rules, true);
    }

    /// <summary>Nom de champ de tri Paperless (<c>created</c>, <c>custom_field_3</c>…), ou null.</summary>
    private static (JsonNode?, bool) SortField(JsonNode? value)
    {
        if (value is null)
        {
            return (null, true);
        }

        return value is JsonValue v && v.TryGetValue<string>(out var text)
            && text.Length is > 0 and <= MaxSortFieldLength && char.IsAsciiLetterLower(text[0])
            && text.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
                ? (JsonValue.Create(text), true)
                : (null, false);
    }

    /// <summary>
    /// Ajoute ou retire la vue des listes du tableau de bord et du menu de l'utilisateur ; null si
    /// rien n'est demandé ou si l'enregistrement réussit.
    /// </summary>
    private static async Task<IResult?> UpdateVisibilityAsync(
        HttpContext context, PaperlessRelay relay, int id, (bool? Dashboard, bool? Sidebar) visibility, CancellationToken cancellationToken)
    {
        if (visibility is (null, null))
        {
            return null;
        }

        JsonObject? settings = null;
        var read = await ReadSettingsAsync(context, relay, s => settings = s, cancellationToken);
        if (read is not null)
        {
            return read;
        }

        foreach (var key in ServerSettings)
        {
            settings!.Remove(key);
        }

        if (settings!["saved_views"] is not JsonObject saved)
        {
            saved = [];
            settings["saved_views"] = saved;
        }

        Toggle(saved, DashboardIds, id, visibility.Dashboard);
        Toggle(saved, SidebarIds, id, visibility.Sidebar);

        return await relay.SendAsync(
            context, HttpMethod.Post, "api/ui_settings/", new JsonObject { ["settings"] = settings },
            (_, _) => Task.FromResult<IResult?>(null),
            cancellationToken);
    }

    /// <summary>Lit les préférences de l'utilisateur (<c>settings</c> de ui_settings, détaché de la réponse).</summary>
    private static Task<IResult?> ReadSettingsAsync(
        HttpContext context, PaperlessRelay relay, Action<JsonObject> onSettings, CancellationToken cancellationToken) =>
        relay.SendAsync(
            context, HttpMethod.Get, "api/ui_settings/", null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
                onSettings(body?["settings"]?.DeepClone() as JsonObject ?? []);
                return null;
            },
            cancellationToken);

    /// <summary>Identifiants d'une liste de préférences ; Paperless accepte aussi des chaînes de chiffres.</summary>
    internal static List<int> Ids(JsonObject? saved, string key)
    {
        var ids = new List<int>();
        foreach (var node in saved?[key] as JsonArray ?? [])
        {
            if (node is not JsonValue value)
            {
                continue;
            }

            if (value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number))
            {
                ids.Add(number);
            }
            else if (value.TryGetValue<string>(out var text)
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                ids.Add(parsed);
            }
        }

        return ids;
    }

    private static void Toggle(JsonObject saved, string key, int id, bool? visible)
    {
        if (visible is null)
        {
            return;
        }

        var ids = Ids(saved, key).ToHashSet();
        if (visible.Value)
        {
            ids.Add(id);
        }
        else
        {
            ids.Remove(id);
        }

        saved[key] = new JsonArray([.. ids.Order().Select(i => (JsonNode)i)]);
    }

    /// <summary>Vues visibles, dans l'ordre enregistré, puis les autres ; sans les vues supprimées.</summary>
    internal static JsonArray Ordered(JsonObject? saved, string visibleKey, string orderKey, HashSet<int> existing)
    {
        var visible = Ids(saved, visibleKey).Where(existing.Contains).ToHashSet();
        var order = Ids(saved, orderKey).Where(visible.Contains).Distinct().ToList();
        return new JsonArray([.. order.Concat(visible.Except(order).Order()).Select(i => (JsonNode)i)]);
    }

    private static JsonObject Project(JsonObject source)
    {
        var target = new JsonObject();
        foreach (var field in Fields)
        {
            if (source.TryGetPropertyValue(field, out var value))
            {
                target[field] = value?.DeepClone();
            }
        }

        return target;
    }
}
