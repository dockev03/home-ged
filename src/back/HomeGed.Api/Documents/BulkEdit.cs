using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeGed.Api.Documents;

/// <summary>
/// Actions en masse sur une sélection de documents (UI-06) : liste blanche des opérations
/// <c>bulk_edit</c> de Paperless que Home-GED propose, paramètres vérifiés et normalisés.
/// Les droits sur chaque document restent appliqués par Paperless.
/// </summary>
public static class BulkEdit
{
    /// <summary>Plafond d'une sélection (celui de la sélection « tout »).</summary>
    public const int MaxDocuments = DocumentEndpoints.MaxIds;

    private const int MaxItems = 500;

    /// <summary>Contenu d'une archive téléchargée : nom Home-GED → valeur Paperless.</summary>
    internal static readonly IReadOnlyDictionary<string, string> DownloadContents = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["archive"] = "archive",
        ["originaux"] = "originals",
        ["les_deux"] = "both",
    };

    /// <summary>Corps <c>bulk_edit</c> à transmettre à Paperless, ou le message d'erreur.</summary>
    public static (JsonObject? Request, string? Error) Sanitize(JsonNode? body)
    {
        if (body is not JsonObject source || source.Any(p => p.Key is not ("documents" or "method" or "parameters")))
        {
            return (null, "Requête invalide.");
        }

        var documents = Documents(source["documents"]);
        if (documents is null)
        {
            return (null, string.Create(CultureInfo.InvariantCulture, $"Sélection vide ou trop grande ({MaxDocuments} documents au plus)."));
        }

        var method = source["method"] is JsonValue m && m.TryGetValue<string>(out var name) ? name : null;
        var parameters = source["parameters"] ?? new JsonObject();
        if (parameters is not JsonObject input)
        {
            return (null, "Paramètres invalides.");
        }

        var clean = method switch
        {
            "modify_tags" => ModifyTags(input),
            "set_correspondent" => SetReference(input, "correspondent"),
            "set_document_type" => SetReference(input, "document_type"),
            "set_storage_path" => SetReference(input, "storage_path"),
            "modify_custom_fields" => ModifyCustomFields(input),
            "set_permissions" => SetPermissions(input),
            "delete" => input.Count == 0 ? new JsonObject() : null,
            _ => null,
        };
        if (clean is null)
        {
            return (null, method is null ? "Action manquante." : "Action inconnue ou paramètres invalides.");
        }

        return (new JsonObject { ["documents"] = documents, ["method"] = method, ["parameters"] = clean }, null);
    }

    /// <summary>
    /// Corps <c>bulk_download</c> : sélection et contenu de l'archive (PDF/A d'archive, originaux ou
    /// les deux), ou le message d'erreur.
    /// </summary>
    public static (JsonObject? Request, string? Error) SanitizeDownload(JsonNode? body)
    {
        if (body is not JsonObject source || source.Any(p => p.Key is not ("documents" or "contenu")))
        {
            return (null, "Requête invalide.");
        }

        var documents = Documents(source["documents"]);
        if (documents is null)
        {
            return (null, string.Create(CultureInfo.InvariantCulture, $"Sélection vide ou trop grande ({MaxDocuments} documents au plus)."));
        }

        var content = "archive";
        if (source.TryGetPropertyValue("contenu", out var requested))
        {
            if (requested is not JsonValue v || !v.TryGetValue<string>(out var text) || !DownloadContents.TryGetValue(text, out var paperless))
            {
                return (null, "Contenu de l'archive invalide.");
            }

            content = paperless;
        }

        return (new JsonObject { ["documents"] = documents, ["content"] = content, ["follow_formatting"] = false }, null);
    }

    private static JsonArray? Documents(JsonNode? node)
    {
        var ids = Ids(node, MaxDocuments);
        return ids is { Count: > 0 } ? ids : null;
    }

    private static JsonObject? ModifyTags(JsonObject input)
    {
        if (input.Any(p => p.Key is not ("add_tags" or "remove_tags")))
        {
            return null;
        }

        var add = Ids(input["add_tags"] ?? new JsonArray(), MaxItems);
        var remove = Ids(input["remove_tags"] ?? new JsonArray(), MaxItems);
        return add is null || remove is null || add.Count + remove.Count == 0
            ? null
            : new JsonObject { ["add_tags"] = add, ["remove_tags"] = remove };
    }

    /// <summary>Correspondant, type ou chemin : un identifiant, ou null pour le retirer.</summary>
    private static JsonObject? SetReference(JsonObject input, string key)
    {
        if (input.Count != 1 || !input.TryGetPropertyValue(key, out var value))
        {
            return null;
        }

        if (value is null)
        {
            return new JsonObject { [key] = null };
        }

        return Id(value) is { } id ? new JsonObject { [key] = id } : null;
    }

    /// <summary>
    /// Champs ajoutés avec leur valeur (<c>[{ field, value }]</c> côté Home-GED, objet
    /// <c>{ "id": valeur }</c> côté Paperless) et champs retirés. La valeur est validée par Paperless.
    /// </summary>
    private static JsonObject? ModifyCustomFields(JsonObject input)
    {
        if (input.Any(p => p.Key is not ("add_custom_fields" or "remove_custom_fields")))
        {
            return null;
        }

        if ((input["add_custom_fields"] ?? new JsonArray()) is not JsonArray added || added.Count > MaxItems)
        {
            return null;
        }

        var add = new JsonObject();
        foreach (var item in added)
        {
            if (item is not JsonObject obj || obj.Any(p => p.Key is not ("field" or "value"))
                || Id(obj["field"]) is not { } field
                || !IsFieldValue(obj["value"])
                || !add.TryAdd(field.ToString(CultureInfo.InvariantCulture), obj["value"]?.DeepClone()))
            {
                return null;
            }
        }

        var remove = Ids(input["remove_custom_fields"] ?? new JsonArray(), MaxItems);
        if (remove is null || add.Count + remove.Count == 0)
        {
            return null;
        }

        return new JsonObject { ["add_custom_fields"] = add, ["remove_custom_fields"] = remove };
    }

    /// <summary>Valeur simple, ou liste d'identifiants (lien vers des documents).</summary>
    private static bool IsFieldValue(JsonNode? value) => value switch
    {
        null => true,
        JsonValue v => v.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False,
        JsonArray array => Ids(array, MaxItems) is not null,
        _ => false,
    };

    /// <summary>
    /// Propriétaire et droits : mêmes règles que la fiche ; <c>merge</c> ajoute les droits à ceux
    /// existants au lieu de les remplacer.
    /// </summary>
    private static JsonObject? SetPermissions(JsonObject input)
    {
        var merge = false;
        var permissions = new JsonObject();
        foreach (var (key, value) in input)
        {
            if (key == "merge")
            {
                if (value is not JsonValue v || v.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return null;
                }

                merge = v.GetValue<bool>();
            }
            else
            {
                permissions[key] = value?.DeepClone();
            }
        }

        var (update, _) = DocumentUpdate.SanitizePermissions(permissions);
        if (update is null)
        {
            return null;
        }

        update["merge"] = merge;
        return update;
    }

    private static int? Id(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var id) && id > 0 ? id : null;

    /// <summary>Identifiants strictement positifs, sans doublon, dans l'ordre reçu.</summary>
    private static JsonArray? Ids(JsonNode? node, int max)
    {
        if (node is not JsonArray array || array.Count > max)
        {
            return null;
        }

        var seen = new HashSet<int>();
        var ids = new JsonArray();
        foreach (var item in array)
        {
            if (Id(item) is not { } id)
            {
                return null;
            }

            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
