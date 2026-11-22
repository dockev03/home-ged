using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HomeGed.IntegrationTests;

/// <summary>
/// Vues sauvegardées (UI-08) et tableau de bord (UI-09) à travers Home-GED, contre un Paperless réel :
/// règles produites par le front, préférences d'affichage dans ui_settings, statistiques et échéances.
/// </summary>
[Collection(BaseHomeGed.Name)]
public sealed class SavedViewIntegrationTests(HomeGedAppFixture app) : IClassFixture<HomeGedAppFixture>
{
    private static int[] Ids(JsonElement list, string key) => [.. list.GetProperty(key).EnumerateArray().Select(e => e.GetInt32())];

    [Fact]
    public async Task Vue_creee_affichee_modifiee_puis_supprimee()
    {
        // Toutes les règles que le front sait produire (filters/savedViews.ts), sur des valeurs réalistes.
        var user = app.UserId.ToString(CultureInfo.InvariantCulture);
        object[] rules =
        [
            new { rule_type = 19, value = app.Marker },
            new { rule_type = 6, value = "999999" },
            new { rule_type = 22, value = "999999" },
            new { rule_type = 7, value = "false" },
            new { rule_type = 3, value = (string?)null },
            new { rule_type = 26, value = "999999" },
            new { rule_type = 28, value = "999999" },
            new { rule_type = 30, value = "999999" },
            new { rule_type = 38, value = "999999" },
            new { rule_type = 36, value = "EDF" },
            new { rule_type = 44, value = "2000-01-01" },
            new { rule_type = 43, value = "2100-01-01" },
            new { rule_type = 46, value = "2000-01-01" },
            new { rule_type = 45, value = "2100-01-01" },
            new { rule_type = 32, value = user },
            new { rule_type = 35, value = user },
            new { rule_type = 34, value = "false" },
        ];

        using var created = await app.SendAsync(HttpMethod.Post, "/api/vues", new
        {
            name = "Vue " + app.Marker,
            filter_rules = rules,
            sort_field = "score",
            sort_reverse = true,
            tableau_de_bord = true,
            menu = true,
        });
        var createdText = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdText);
        var id = JsonElement.Parse(createdText).GetProperty("id").GetInt32();

        try
        {
            var list = await app.GetJsonAsync("/api/vues");
            var view = list.GetProperty("vues").EnumerateArray().Single(v => v.GetProperty("id").GetInt32() == id);
            Assert.Equal(rules.Length, view.GetProperty("filter_rules").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, view.GetProperty("filter_rules")[4].GetProperty("value").ValueKind);
            Assert.Contains(id, Ids(list, "tableau_de_bord"));
            Assert.Contains(id, Ids(list, "menu"));

            using var updated = await app.SendAsync(HttpMethod.Patch, $"/api/vues/{id}", new
            {
                name = "Renommée " + app.Marker,
                filter_rules = new[] { new { rule_type = 19, value = app.Marker } },
                sort_field = "created",
                tableau_de_bord = false,
            });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            list = await app.GetJsonAsync("/api/vues");
            view = list.GetProperty("vues").EnumerateArray().Single(v => v.GetProperty("id").GetInt32() == id);
            Assert.Equal("Renommée " + app.Marker, view.GetProperty("name").GetString());
            Assert.Equal(1, view.GetProperty("filter_rules").GetArrayLength());
            Assert.DoesNotContain(id, Ids(list, "tableau_de_bord"));
            Assert.Contains(id, Ids(list, "menu"));

            // Les critères de la vue, traduits par le front, retrouvent le document importé.
            var page = await app.GetJsonAsync($"/api/documents?title_content={app.Marker}&ordering=-created");
            Assert.Equal(1, page.GetProperty("count").GetInt32());
        }
        finally
        {
            using var deleted = await app.SendAsync(HttpMethod.Delete, $"/api/vues/{id}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        var after = await app.GetJsonAsync("/api/vues");
        Assert.DoesNotContain(after.GetProperty("vues").EnumerateArray(), v => v.GetProperty("id").GetInt32() == id);
        Assert.DoesNotContain(id, Ids(after, "menu"));
    }

    [Fact]
    public async Task Statistiques()
    {
        var statistics = await app.GetJsonAsync("/api/tableau-de-bord/statistiques");

        Assert.True(statistics.GetProperty("documents_total").GetInt32() >= 1);
        Assert.True(statistics.TryGetProperty("tag_count", out _));
    }

    [Fact]
    public async Task Echeance_a_venir_trouvee_par_le_champ_de_type_date()
    {
        var existing = await FindDeadlineFieldAsync();
        var fieldId = existing;
        if (fieldId is null)
        {
            using var field = await app.SendAsync(HttpMethod.Post, "/api/gestion/champs", new { name = "Échéance", data_type = "date" });
            Assert.Equal(HttpStatusCode.Created, field.StatusCode);
            fieldId = (await field.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        try
        {
            var deadline = DateTime.Today.AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using var set = await app.SendAsync(HttpMethod.Patch, $"/api/documents/{app.DocumentId}", new
            {
                custom_fields = new[] { new { field = fieldId, value = deadline } },
            });
            Assert.True(set.IsSuccessStatusCode, await set.Content.ReadAsStringAsync());

            var deadlines = await app.GetJsonAsync("/api/tableau-de-bord/echeances?nombre=25");

            Assert.Equal(fieldId, deadlines.GetProperty("champ").GetProperty("id").GetInt32());
            var item = deadlines.GetProperty("results").EnumerateArray().Single(d => d.GetProperty("id").GetInt32() == app.DocumentId);
            Assert.Equal(deadline, item.GetProperty("echeance").GetString());
        }
        finally
        {
            using var cleared = await app.SendAsync(HttpMethod.Patch, $"/api/documents/{app.DocumentId}", new { custom_fields = Array.Empty<object>() });
            if (existing is null)
            {
                using var _ = await app.SendAsync(HttpMethod.Delete, $"/api/gestion/champs/{fieldId}");
            }
        }
    }

    private async Task<int?> FindDeadlineFieldAsync()
    {
        var fields = await app.GetJsonAsync("/api/gestion/champs?nom=%C3%89ch%C3%A9ance&page_size=100");
        foreach (var field in fields.GetProperty("results").EnumerateArray())
        {
            if (string.Equals(field.GetProperty("name").GetString(), "Échéance", StringComparison.OrdinalIgnoreCase)
                && field.GetProperty("data_type").GetString() == "date")
            {
                return field.GetProperty("id").GetInt32();
            }
        }

        return null;
    }
}
