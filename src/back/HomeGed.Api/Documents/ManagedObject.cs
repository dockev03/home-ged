using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeGed.Api.Documents;

/// <summary>
/// Liste blanche des champs modifiables depuis les écrans « Gérer » (UI-07) : étiquettes,
/// correspondants, types de documents, chemins de stockage et champs personnalisés. Les types et
/// bornes sont vérifiés ici ; l'unicité des noms et la syntaxe des modèles de chemin restent à Paperless.
/// </summary>
public static class ManagedObject
{
    public const int MaxNameLength = 128;

    /// <summary>Longueur maximale du texte de correspondance dans Paperless.</summary>
    public const int MaxMatchLength = 256;

    public const int MaxPathLength = 2048;

    public const int MaxSelectOptions = 500;

    public const int MaxOptionLabelLength = 128;

    /// <summary>Algorithmes de correspondance de Paperless : 0 (aucun) à 6 (automatique).</summary>
    public const int MaxMatchingAlgorithm = 6;

    /// <summary>Types de champ personnalisé de Paperless.</summary>
    internal static readonly FrozenSet<string> DataTypes = FrozenSet.Create(
        StringComparer.Ordinal,
        "string", "longtext", "url", "date", "boolean", "integer", "float", "monetary", "select", "documentlink");

    /// <summary>Corps à transmettre à Paperless, ou le message d'erreur.</summary>
    /// <param name="kind">Nom Home-GED de la liste (tags, correspondants, types, chemins, champs).</param>
    /// <param name="creating">Création : nom (et chemin, type de champ) obligatoires.</param>
    public static (JsonObject? Body, string? Error) Sanitize(string kind, JsonNode? body, bool creating)
    {
        if (body is not JsonObject source || source.Count == 0)
        {
            return (null, "Aucune modification.");
        }

        var target = new JsonObject();
        var matchable = kind != "champs";
        foreach (var (key, value) in source)
        {
            var (clean, valid) = key switch
            {
                "name" => Text(value, 1, MaxNameLength),
                "match" when matchable => Text(value, 0, MaxMatchLength, trim: false),
                "matching_algorithm" when matchable => Integer(value, 0, MaxMatchingAlgorithm),
                "is_insensitive" when matchable => Boolean(value),
                "color" when kind == "tags" => Color(value),
                "is_inbox_tag" when kind == "tags" => Boolean(value),
                "path" when kind == "chemins" => Text(value, 1, MaxPathLength),
                // Paperless refuse de changer le type d'un champ existant.
                "data_type" when kind == "champs" && creating => DataType(value),
                "extra_data" when kind == "champs" => ExtraData(value),
                "owner" or "set_permissions" when matchable => (null, true),
                _ => (null, false),
            };
            if (!valid)
            {
                return (null, string.Create(CultureInfo.InvariantCulture, $"Champ « {key} » invalide ou non modifiable."));
            }

            if (key is not ("owner" or "set_permissions"))
            {
                target[key] = clean;
            }
        }

        if (source.ContainsKey("owner") || source.ContainsKey("set_permissions"))
        {
            var permissions = new JsonObject();
            foreach (var key in (string[])["owner", "set_permissions"])
            {
                if (source.TryGetPropertyValue(key, out var value))
                {
                    permissions[key] = value?.DeepClone();
                }
            }

            var (clean, error) = DocumentUpdate.SanitizePermissions(permissions);
            if (clean is null)
            {
                return (null, error);
            }

            foreach (var (key, value) in clean)
            {
                target[key] = value?.DeepClone();
            }
        }

        if (creating)
        {
            var missing = !target.ContainsKey("name")
                || (kind == "chemins" && !target.ContainsKey("path"))
                || (kind == "champs" && !target.ContainsKey("data_type"));
            if (missing)
            {
                return (null, kind switch
                {
                    "chemins" => "Nom et chemin obligatoires.",
                    "champs" => "Nom et type de données obligatoires.",
                    _ => "Nom obligatoire.",
                });
            }
        }

        return (target, null);
    }

    internal static (JsonNode?, bool) Text(JsonNode? value, int min, int max, bool trim = true)
    {
        if (value is not JsonValue v || !v.TryGetValue<string>(out var text))
        {
            return (null, false);
        }

        if (trim)
        {
            text = text.Trim();
        }

        return text.Length >= min && text.Length <= max ? (JsonValue.Create(text), true) : (null, false);
    }

    internal static (JsonNode?, bool) Integer(JsonNode? value, int min, int max) =>
        value is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var n) && n >= min && n <= max
            ? (JsonValue.Create(n), true)
            : (null, false);

    internal static (JsonNode?, bool) Boolean(JsonNode? value) =>
        value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? (JsonValue.Create(v.GetValue<bool>()), true)
            : (null, false);

    private static (JsonNode?, bool) Color(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var text) && IsHexColor(text.Trim())
            ? (JsonValue.Create(text.Trim().ToLowerInvariant()), true)
            : (null, false);

    internal static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);

    private static (JsonNode?, bool) DataType(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var text) && DataTypes.Contains(text)
            ? (JsonValue.Create(text), true)
            : (null, false);

    /// <summary>
    /// Options d'une liste de choix (<c>select_options</c> : identifiant conservé pour une option
    /// existante, absent pour une nouvelle) et devise par défaut d'un champ monétaire.
    /// </summary>
    private static (JsonNode?, bool) ExtraData(JsonNode? value)
    {
        if (value is null)
        {
            return (null, true);
        }

        if (value is not JsonObject source || source.Any(p => p.Key is not ("select_options" or "default_currency")))
        {
            return (null, false);
        }

        var extra = new JsonObject();
        if (source.TryGetPropertyValue("select_options", out var options))
        {
            if (options is not JsonArray array || array.Count > MaxSelectOptions)
            {
                return (null, false);
            }

            var clean = new JsonArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (item is not JsonObject option || option.Any(p => p.Key is not ("id" or "label"))
                    || Text(option["label"], 1, MaxOptionLabelLength) is not ({ } label, true))
                {
                    return (null, false);
                }

                var cleanOption = new JsonObject { ["label"] = label };
                if (option["id"] is { } idNode)
                {
                    if (Text(idNode, 1, 64) is not ({ } id, true) || !ids.Add(id.GetValue<string>()))
                    {
                        return (null, false);
                    }

                    cleanOption["id"] = id;
                }

                clean.Add(cleanOption);
            }

            extra["select_options"] = clean;
        }

        if (source.TryGetPropertyValue("default_currency", out var currency))
        {
            if (currency is null)
            {
                extra["default_currency"] = null;
            }
            else if (currency is JsonValue c && c.TryGetValue<string>(out var code)
                && code.Trim().ToUpperInvariant() is { Length: 3 } upper && upper.All(char.IsAsciiLetterUpper))
            {
                extra["default_currency"] = upper;
            }
            else if (currency is JsonValue empty && empty.TryGetValue<string>(out var blank) && blank.Trim().Length == 0)
            {
                extra["default_currency"] = null;
            }
            else
            {
                return (null, false);
            }
        }

        return (extra, true);
    }
}
