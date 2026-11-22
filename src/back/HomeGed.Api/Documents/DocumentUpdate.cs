using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeGed.Api.Documents;

/// <summary>
/// Liste blanche des champs modifiables depuis la fiche (UI-05). Les types sont vérifiés ici ;
/// les règles métier (étiquette existante, valeur d'un champ personnalisé…) restent à Paperless.
/// </summary>
public static class DocumentUpdate
{
    public const int MaxTitleLength = 128;

    /// <summary>Plafond du numéro d'archive dans Paperless (entier non signé 32 bits).</summary>
    public const long MaxArchiveSerialNumber = 0xFFFFFFFF;

    private const int MaxItems = 500;

    /// <summary>Corps à transmettre à Paperless, ou le message d'erreur.</summary>
    public static (JsonObject? Update, string? Error) Sanitize(JsonNode? body)
    {
        if (body is not JsonObject source || source.Count == 0)
        {
            return (null, "Aucune modification.");
        }

        var update = new JsonObject();
        foreach (var (key, value) in source)
        {
            var (clean, valid) = key switch
            {
                "title" => Title(value),
                "archive_serial_number" => NullableInteger(value, 0, MaxArchiveSerialNumber),
                "created" => Date(value),
                "correspondent" or "document_type" or "storage_path" => NullableInteger(value, 1, int.MaxValue),
                "tags" => Ids(value),
                "custom_fields" => CustomFields(value),
                _ => (null, false),
            };
            if (!valid)
            {
                return (null, string.Create(CultureInfo.InvariantCulture, $"Champ « {key} » invalide ou non modifiable."));
            }

            update[key] = clean;
        }

        return (update, null);
    }

    private static (JsonNode?, bool) Title(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var text) && text.Trim() is { Length: > 0 and <= MaxTitleLength } title
            ? (JsonValue.Create(title), true)
            : (null, false);

    private static (JsonNode?, bool) NullableInteger(JsonNode? value, long min, long max)
    {
        if (value is null)
        {
            return (null, true);
        }

        return value is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var n) && n >= min && n <= max
            ? (JsonValue.Create(n), true)
            : (null, false);
    }

    private static (JsonNode?, bool) Date(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var text)
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? (JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), true)
            : (null, false);

    private static (JsonNode?, bool) Ids(JsonNode? value)
    {
        if (value is not JsonArray array || array.Count > MaxItems)
        {
            return (null, false);
        }

        var ids = new SortedSet<int>();
        foreach (var item in array)
        {
            if (item is not JsonValue v || v.GetValueKind() != JsonValueKind.Number || !v.TryGetValue<int>(out var id) || id < 1)
            {
                return (null, false);
            }

            ids.Add(id);
        }

        return (new JsonArray([.. ids.Select(id => (JsonNode)JsonValue.Create(id))]), true);
    }

    /// <summary>Liste de <c>{ field, value }</c> ; la valeur est validée par Paperless selon le type du champ.</summary>
    private static (JsonNode?, bool) CustomFields(JsonNode? value)
    {
        if (value is not JsonArray array || array.Count > MaxItems)
        {
            return (null, false);
        }

        var fields = new JsonArray();
        var seen = new HashSet<int>();
        foreach (var item in array)
        {
            if (item is not JsonObject obj
                || obj["field"] is not JsonValue f || f.GetValueKind() != JsonValueKind.Number
                || !f.TryGetValue<int>(out var field) || field < 1 || !seen.Add(field)
                || obj.Count > 2 || (obj.Count == 2 && !obj.ContainsKey("value")))
            {
                return (null, false);
            }

            var fieldValue = obj["value"];
            if (fieldValue is JsonObject)
            {
                return (null, false);
            }

            fields.Add(new JsonObject { ["field"] = field, ["value"] = fieldValue?.DeepClone() });
        }

        return (fields, true);
    }
}
