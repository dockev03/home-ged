using System.Net;
using System.Text.Json;

namespace HomeGed.IntegrationTests;

/// <summary>
/// Corbeille (UI-10) et suggestions (UI-12) à travers Home-GED, contre un Paperless réel. Seul le
/// document créé par le test est effacé : « vider la corbeille » n'est pas joué, il toucherait les
/// documents des autres tests.
/// </summary>
[Collection(BaseHomeGed.Name)]
public sealed class TrashIntegrationTests(HomeGedAppFixture app) : IClassFixture<HomeGedAppFixture>
{
    private async Task<JsonElement?> InTrashAsync(int id)
    {
        var page = await app.GetJsonAsync("/api/corbeille");
        Assert.True(page.GetProperty("delai").ValueKind is JsonValueKind.Number or JsonValueKind.Null);
        foreach (var item in page.GetProperty("results").EnumerateArray())
        {
            if (item.GetProperty("id").GetInt32() == id)
            {
                return item;
            }
        }

        return null;
    }

    [Fact]
    public async Task Suggestions_puis_corbeille_restauration_et_effacement_definitif()
    {
        using var form = HomeGedAppFixture.TextFile("document", $"corbeille-{app.Marker}.txt", $"Corbeille {app.Marker} {Guid.NewGuid():N}\nÉchéance le 12/03/2026");
        using var upload = await app.SendAsync(HttpMethod.Post, "/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var id = await app.WaitForDocumentAsync(JsonElement.Parse(await upload.Content.ReadAsStringAsync()).GetProperty("tache").GetGuid());
        var erased = false;
        try
        {
            var suggestions = await app.GetJsonAsync($"/api/documents/{id}/suggestions");
            foreach (var list in new[] { "tags", "correspondents", "document_types", "storage_paths", "dates" })
            {
                Assert.Equal(JsonValueKind.Array, suggestions.GetProperty(list).ValueKind);
            }

            Assert.Contains("2026-03-12", suggestions.GetProperty("dates").EnumerateArray().Select(d => d.GetString()));

            using (var deleted = await app.SendAsync(HttpMethod.Delete, $"/api/documents/{id}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            }

            var trashed = await InTrashAsync(id);
            Assert.NotNull(trashed);
            Assert.False(trashed.Value.TryGetProperty("content", out _));
            Assert.Equal(JsonValueKind.String, trashed.Value.GetProperty("deleted_at").ValueKind);

            using (var restored = await app.SendAsync(HttpMethod.Post, "/api/corbeille/restauration", new { documents = new[] { id } }))
            {
                Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
            }

            Assert.Null(await InTrashAsync(id));
            using (var document = await app.SendAsync(HttpMethod.Get, $"/api/documents/{id}"))
            {
                Assert.Equal(HttpStatusCode.OK, document.StatusCode);
            }

            // Déjà restauré : Paperless refuse, rien n'est effacé.
            using (var again = await app.SendAsync(HttpMethod.Post, "/api/corbeille/suppression", new { documents = new[] { id } }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
            }

            using (var deleted = await app.SendAsync(HttpMethod.Delete, $"/api/documents/{id}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            }

            using (var emptied = await app.SendAsync(HttpMethod.Post, "/api/corbeille/suppression", new { documents = new[] { id } }))
            {
                Assert.Equal(HttpStatusCode.NoContent, emptied.StatusCode);
            }

            erased = true;
            Assert.Null(await InTrashAsync(id));
            using var gone = await app.SendAsync(HttpMethod.Get, $"/api/documents/{id}");
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }
        finally
        {
            if (!erased)
            {
                using var _ = await app.SendAsync(HttpMethod.Delete, $"/api/documents/{id}");
            }
        }
    }
}
