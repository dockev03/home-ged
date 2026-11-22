using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace HomeGed.Api.Documents;

/// <summary>
/// Données du tableau de bord (UI-09) qui ne passent pas par la liste des documents : statistiques
/// de Paperless et échéances à venir. Toujours avec le token de l'utilisateur : seuls les documents
/// qu'il peut voir sont comptés.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>Nom du champ personnalisé de type date qui porte les échéances (configuration de Paperless, F1).</summary>
    public const string DeadlineFieldName = "Échéance";

    public const int DefaultDeadlineCount = 10;

    public const int MaxDeadlineCount = 25;

    private static readonly string[] StatisticsFields =
    [
        "documents_total", "documents_inbox", "inbox_tags", "tag_count", "correspondent_count",
        "document_type_count", "storage_path_count", "character_count", "document_file_type_counts",
    ];

    public static RouteGroupBuilder MapDashboard(this RouteGroupBuilder api)
    {
        api.MapGet("/tableau-de-bord/statistiques", StatisticsAsync);
        api.MapGet("/tableau-de-bord/echeances", DeadlinesAsync);
        return api;
    }

    private static async Task<IResult> StatisticsAsync(HttpContext context, PaperlessRelay relay, CancellationToken cancellationToken) =>
        (await relay.SendAsync(
            context, HttpMethod.Get, "api/statistics/", null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                var statistics = new JsonObject();
                foreach (var field in StatisticsFields)
                {
                    if (body.TryGetPropertyValue(field, out var value))
                    {
                        statistics[field] = value?.DeepClone();
                    }
                }

                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Json(statistics);
            },
            cancellationToken))!;

    /// <summary>
    /// Prochaines échéances : documents dont le champ « Échéance » est aujourd'hui ou plus tard, la plus
    /// proche d'abord. Sans ce champ (ou s'il n'est pas de type date), réponse vide avec <c>champ</c> à null.
    /// </summary>
    private static async Task<IResult> DeadlinesAsync(
        HttpContext context, PaperlessRelay relay, TimeProvider time, CancellationToken cancellationToken)
    {
        var count = int.TryParse(context.Request.Query["nombre"], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 1, MaxDeadlineCount)
            : DefaultDeadlineCount;

        JsonObject? field = null;
        var found = await relay.SendAsync(
            context, HttpMethod.Get,
            "api/custom_fields/" + QueryString.Create(new KeyValuePair<string, string?>[] { new("name__iexact", DeadlineFieldName), new("page_size", "10") }).ToUriComponent(),
            null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                field = (body["results"] as JsonArray ?? []).OfType<JsonObject>()
                    .FirstOrDefault(f => f["data_type"]?.GetValue<string>() == "date" && f["id"] is JsonValue);
                return null;
            },
            cancellationToken);
        if (found is not null)
        {
            return found;
        }

        context.Response.Headers.CacheControl = "no-store";
        if (field is null)
        {
            return TypedResults.Json(new JsonObject { ["champ"] = null, ["count"] = 0, ["results"] = new JsonArray() });
        }

        var id = field["id"]!.GetValue<int>();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = QueryString.Create(new KeyValuePair<string, string?>[]
        {
            new("custom_field_query", new JsonArray(id, "gte", today).ToJsonString()),
            new("ordering", string.Create(CultureInfo.InvariantCulture, $"custom_field_{id}")),
            new("page_size", count.ToString(CultureInfo.InvariantCulture)),
            new("truncate_content", "true"),
        });

        return (await relay.SendAsync(
            context, HttpMethod.Get, "api/documents/" + query.ToUriComponent(), null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                    ?? throw new HttpRequestException("Réponse de Paperless vide.");
                var results = new JsonArray();
                foreach (var document in (body["results"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var deadline = (document["custom_fields"] as JsonArray ?? []).OfType<JsonObject>()
                        .FirstOrDefault(v => v["field"] is JsonValue f && f.TryGetValue<int>(out var fid) && fid == id)?["value"];
                    results.Add(new JsonObject
                    {
                        ["id"] = document["id"]?.DeepClone(),
                        ["title"] = document["title"]?.DeepClone(),
                        ["correspondent"] = document["correspondent"]?.DeepClone(),
                        ["tags"] = document["tags"]?.DeepClone() ?? new JsonArray(),
                        ["echeance"] = deadline?.DeepClone(),
                    });
                }

                return TypedResults.Json(new JsonObject
                {
                    ["champ"] = new JsonObject { ["id"] = id, ["name"] = field["name"]?.DeepClone() },
                    ["count"] = body["count"]?.DeepClone() ?? 0,
                    ["results"] = results,
                });
            },
            cancellationToken,
            isFilter: true))!;
    }
}
