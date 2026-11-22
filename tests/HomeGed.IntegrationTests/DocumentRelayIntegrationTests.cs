using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeGed.Api;
using HomeGed.Api.Auth;
using HomeGed.Api.Paperless;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HomeGed.IntegrationTests;

/// <summary>
/// Home-GED complet (BFF + base homeged) devant un Paperless réel : un document texte est importé
/// dans Paperless, puis lu, filtré, modifié et versionné à travers les relais de Home-GED.
/// </summary>
public sealed class HomeGedAppFixture : IAsyncLifetime
{
    private WebApplication? _app;

    private string _cookie = "";

    public HttpClient Http { get; private set; } = null!;

    /// <summary>Accès direct à Paperless (préparation du jeu d'essai uniquement).</summary>
    public HttpClient Paperless { get; private set; } = null!;

    public string Marker { get; } = "homeged" + Guid.NewGuid().ToString("N")[..12];

    public int DocumentId { get; private set; }

    public int UserId { get; private set; }

    public async Task InitializeAsync()
    {
        _app = HomeGedApp.Create(
            [
                $"--Paperless:BaseUrl={IntegrationEnvironment.PaperlessUrl}",
                $"--Database:ConnectionString={IntegrationEnvironment.HomeGedDb}",
            ],
            b => b.WebHost.UseUrls("http://127.0.0.1:0"));
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        // Cookie de session à la main : il est Secure et le test parle HTTP.
        Http = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { BaseAddress = new Uri(address) };

        using var login = await SendAsync(HttpMethod.Post, "/api/auth/login", new
        {
            username = IntegrationEnvironment.PaperlessUser,
            password = IntegrationEnvironment.PaperlessPassword,
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        _cookie = login.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith(SessionTokens.CookieName + "=", StringComparison.Ordinal))
            .Split(';')[0][(SessionTokens.CookieName.Length + 1)..];
        UserId = (await login.Content.ReadFromJsonAsync<CurrentUser>())!.Id;

        Paperless = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        PaperlessClient.Configure(Paperless, new PaperlessOptions { BaseUrl = IntegrationEnvironment.PaperlessUrl });
        Paperless.Timeout = TimeSpan.FromSeconds(60);
        Paperless.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{IntegrationEnvironment.PaperlessUser}:{IntegrationEnvironment.PaperlessPassword}")));

        using var form = TextFile("document", $"facture-{Marker}.txt", $"Facture d'intégration {Marker}\nMontant 84,12 EUR");
        using var upload = await Paperless.PostAsync(new Uri("api/documents/post_document/", UriKind.Relative), form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var task = await upload.Content.ReadFromJsonAsync<string>();
        DocumentId = await WaitForDocumentAsync(Guid.Parse(task!));
    }

    public async Task DisposeAsync()
    {
        if (DocumentId > 0)
        {
            using var _ = await SendAsync(HttpMethod.Delete, $"/api/documents/{DocumentId}");
        }

        Http?.Dispose();
        Paperless?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    public static MultipartFormDataContent TextFile(string field, string name, string text) =>
        new() { { new StringContent(text, Encoding.UTF8, "text/plain"), field, name } };

    /// <summary>Requête au BFF avec la session et l'en-tête anti-CSRF.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        if (_cookie.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{SessionTokens.CookieName}={_cookie}");
        }

        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body),
        };
        return await Http.SendAsync(request);
    }

    public async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path} : HTTP {(int)response.StatusCode} {text}");
        return JsonElement.Parse(text);
    }

    /// <summary>Attend la fin du traitement Paperless (OCR, classement) et renvoie le document créé.</summary>
    public async Task<int> WaitForDocumentAsync(Guid task)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var state = await GetJsonAsync($"/api/taches/{task}");
            var status = state.GetProperty("status").GetString();
            if (status == "success")
            {
                return state.GetProperty("document_id").GetInt32();
            }

            Assert.True(status is "pending" or "started", $"Tâche {task} : {status} {state.GetProperty("message")}");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Tâche Paperless {task} non terminée après 2 minutes.");
    }
}

[Collection(BaseHomeGed.Name)]
public sealed class DocumentRelayIntegrationTests(HomeGedAppFixture app) : IClassFixture<HomeGedAppFixture>
{
    [Fact]
    public async Task Le_document_importe_se_retrouve_par_les_filtres()
    {
        var marker = app.Marker;
        var found = await app.GetJsonAsync($"/api/documents?title_content={marker}&ordering=-created");
        Assert.Equal(1, found.GetProperty("count").GetInt32());
        Assert.Equal(app.DocumentId, found.GetProperty("results")[0].GetProperty("id").GetInt32());
        var ids = await app.GetJsonAsync($"/api/documents/ids?title_content={marker}&ordering=-created");
        Assert.Equal([app.DocumentId], ids.EnumerateArray().Select(e => e.GetInt32()));

        var fullText = await app.GetJsonAsync($"/api/documents?query={marker}");
        Assert.Equal(1, fullText.GetProperty("count").GetInt32());
    }

    /// <summary>Tous les paramètres de la barre de filtres (UI-03) sont des filtres connus de Paperless.</summary>
    [Theory]
    [InlineData("title__icontains={m}", true)]
    [InlineData("is_tagged=0", true)]
    [InlineData("correspondent__isnull=1", true)]
    [InlineData("document_type__isnull=1", true)]
    [InlineData("storage_path__isnull=1", true)]
    [InlineData("created__gte=2000-01-01&created__lte=2100-01-01", true)]
    [InlineData("added__date__gte=2000-01-01&added__date__lte=2100-01-01", true)]
    [InlineData("owner__id={u}", true)]
    [InlineData("owner__isnull=1", false)]
    [InlineData("owner__id__none={u}&owner__isnull=0", false)]
    [InlineData("correspondent__id__in=999999", false)]
    [InlineData("document_type__id__in=999999", false)]
    [InlineData("storage_path__id__in=999999", false)]
    [InlineData("tags__id__all=999999", false)]
    [InlineData("tags__id__in=999999", false)]
    [InlineData("custom_fields__id__all=999999", false)]
    [InlineData("custom_fields__icontains={m}", false)]
    [InlineData("ordering=title", true)]
    [InlineData("ordering=-added", true)]
    [InlineData("ordering=correspondent__name", true)]
    [InlineData("ordering=document_type__name", true)]
    [InlineData("ordering=archive_serial_number", true)]
    [InlineData("ordering=modified", true)]
    [InlineData("ordering=num_notes", true)]
    [InlineData("ordering=page_count", true)]
    public async Task Filtre_accepte_par_Paperless(string filter, bool expectFound)
    {
        var query = filter
            .Replace("{m}", app.Marker, StringComparison.Ordinal)
            .Replace("{u}", app.UserId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var page = await app.GetJsonAsync($"/api/documents?title_content={app.Marker}&{query}");

        Assert.Equal(expectFound ? 1 : 0, page.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Fiche_modification_et_creation_a_la_volee()
    {
        using var created = await app.SendAsync(HttpMethod.Post, "/api/references/tags", new { name = "Étiquette " + app.Marker, color = "#1f78b4" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tag = JsonElement.Parse(await created.Content.ReadAsStringAsync()).GetProperty("id").GetInt32();

        using var updated = await app.SendAsync(HttpMethod.Patch, $"/api/documents/{app.DocumentId}", new
        {
            title = "Facture modifiée " + app.Marker,
            tags = new[] { tag },
            archive_serial_number = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var document = await app.GetJsonAsync($"/api/documents/{app.DocumentId}");
        Assert.Equal("Facture modifiée " + app.Marker, document.GetProperty("title").GetString());
        Assert.Equal([tag], document.GetProperty("tags").EnumerateArray().Select(t => t.GetInt32()));
        Assert.Contains(app.Marker, document.GetProperty("content").GetString(), StringComparison.Ordinal);

        var tagged = await app.GetJsonAsync($"/api/documents?tags__id__all={tag}");
        Assert.Equal(1, tagged.GetProperty("count").GetInt32());
        var tags = await app.GetJsonAsync("/api/references/tags");
        Assert.Contains(tags.EnumerateArray(), t => t.GetProperty("id").GetInt32() == tag);
    }

    [Fact]
    public async Task Notes_metadonnees_historique_et_fichiers()
    {
        using var added = await app.SendAsync(HttpMethod.Post, $"/api/documents/{app.DocumentId}/notes", new { note = "Note " + app.Marker });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var notes = await app.GetJsonAsync($"/api/documents/{app.DocumentId}/notes");
        var note = notes.EnumerateArray().Single(n => n.GetProperty("note").GetString() == "Note " + app.Marker);
        using var deleted = await app.SendAsync(HttpMethod.Delete, $"/api/documents/{app.DocumentId}/notes/{note.GetProperty("id").GetInt32()}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var metadata = await app.GetJsonAsync($"/api/documents/{app.DocumentId}/metadata");
        Assert.False(string.IsNullOrEmpty(metadata.GetProperty("original_checksum").GetString()));

        var history = await app.GetJsonAsync($"/api/documents/{app.DocumentId}/history");
        Assert.NotEqual(0, history.GetArrayLength());

        foreach (var file in (string[])["thumb", "preview", "download", "download?original=true"])
        {
            using var response = await app.SendAsync(HttpMethod.Get, $"/api/documents/{app.DocumentId}/{file}");
            Assert.True(response.IsSuccessStatusCode, $"{file} : HTTP {(int)response.StatusCode}");
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task Permissions_utilisateurs_et_groupes()
    {
        var permissions = await app.GetJsonAsync($"/api/documents/{app.DocumentId}/permissions");
        Assert.True(permissions.TryGetProperty("permissions", out _));

        var users = await app.GetJsonAsync("/api/references/utilisateurs");
        var me = users.EnumerateArray().Single(u => u.GetProperty("id").GetInt32() == app.UserId);
        Assert.False(me.TryGetProperty("email", out _));
        Assert.False(me.TryGetProperty("is_superuser", out _));
        await app.GetJsonAsync("/api/references/groupes");

        using var updated = await app.SendAsync(HttpMethod.Patch, $"/api/documents/{app.DocumentId}/permissions", new
        {
            owner = app.UserId,
            set_permissions = new
            {
                view = new { users = new[] { app.UserId }, groups = Array.Empty<int>() },
                change = new { users = Array.Empty<int>(), groups = Array.Empty<int>() },
            },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = JsonElement.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal(app.UserId, after.GetProperty("owner").GetInt32());
        Assert.Equal([app.UserId], after.GetProperty("permissions").GetProperty("view").GetProperty("users").EnumerateArray().Select(u => u.GetInt32()));
    }

    [Fact]
    public async Task Nouvelle_version_envoyee_renommee_puis_supprimee()
    {
        using var form = HomeGedAppFixture.TextFile("document", "version-2.txt", $"Version 2 {app.Marker}");
        form.Add(new StringContent("Version de test"), "version_label");
        using var upload = await app.SendAsync(HttpMethod.Post, $"/api/documents/{app.DocumentId}/versions", form);
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var task = JsonElement.Parse(await upload.Content.ReadAsStringAsync()).GetProperty("tache").GetGuid();
        await app.WaitForDocumentAsync(task);

        var versions = (await app.GetJsonAsync($"/api/documents/{app.DocumentId}")).GetProperty("versions");
        Assert.Equal(2, versions.GetArrayLength());
        var added = versions.EnumerateArray().Single(v => !v.GetProperty("is_root").GetBoolean());
        var versionId = added.GetProperty("id").GetInt32();
        Assert.Equal("Version de test", added.GetProperty("version_label").GetString());

        using var preview = await app.SendAsync(HttpMethod.Get, $"/api/documents/{app.DocumentId}/preview?version={versionId}");
        Assert.Contains("Version 2", await preview.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var renamed = await app.SendAsync(HttpMethod.Patch, $"/api/documents/{app.DocumentId}/versions/{versionId}", new { versionLabel = "Renommée" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        using var deleted = await app.SendAsync(HttpMethod.Delete, $"/api/documents/{app.DocumentId}/versions/{versionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(1, (await app.GetJsonAsync($"/api/documents/{app.DocumentId}")).GetProperty("versions").GetArrayLength());
    }

    [Fact]
    public async Task Actions_en_masse_et_telechargement_groupe()
    {
        using var created = await app.SendAsync(HttpMethod.Post, "/api/references/tags", new { name = "Masse " + app.Marker, color = "#33a02c" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tag = JsonElement.Parse(await created.Content.ReadAsStringAsync()).GetProperty("id").GetInt32();

        using var added = await app.SendAsync(HttpMethod.Post, "/api/documents/actions", new
        {
            documents = new[] { app.DocumentId },
            method = "modify_tags",
            parameters = new { add_tags = new[] { tag }, remove_tags = Array.Empty<int>() },
        });
        var addedBody = await WaitForBulkAsync(added);
        await EventuallyAsync(async () => (await TagsAsync()).Contains(tag), "étiquette ajoutée", addedBody);

        using var removed = await app.SendAsync(HttpMethod.Post, "/api/documents/actions", new
        {
            documents = new[] { app.DocumentId },
            method = "modify_tags",
            parameters = new { add_tags = Array.Empty<int>(), remove_tags = new[] { tag } },
        });
        var removedBody = await WaitForBulkAsync(removed);
        await EventuallyAsync(async () => !(await TagsAsync()).Contains(tag), "étiquette retirée", removedBody);

        using var correspondent = await app.SendAsync(HttpMethod.Post, "/api/documents/actions", new
        {
            documents = new[] { app.DocumentId },
            method = "set_correspondent",
            parameters = new { correspondent = (int?)null },
        });
        await WaitForBulkAsync(correspondent);

        using var permissions = await app.SendAsync(HttpMethod.Post, "/api/documents/actions", new
        {
            documents = new[] { app.DocumentId },
            method = "set_permissions",
            parameters = new
            {
                owner = app.UserId,
                merge = true,
                set_permissions = new
                {
                    view = new { users = Array.Empty<int>(), groups = Array.Empty<int>() },
                    change = new { users = Array.Empty<int>(), groups = Array.Empty<int>() },
                },
            },
        });
        await WaitForBulkAsync(permissions);

        using var zip = await app.SendAsync(HttpMethod.Post, "/api/documents/telechargement", new { documents = new[] { app.DocumentId }, contenu = "originaux" });
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
        Assert.Equal("application/zip", zip.Content.Headers.ContentType!.MediaType);
        Assert.Equal("PK"u8.ToArray(), (await zip.Content.ReadAsByteArrayAsync())[..2]);
    }

    private async Task<int[]> TagsAsync() =>
        [.. (await app.GetJsonAsync($"/api/documents/{app.DocumentId}")).GetProperty("tags").EnumerateArray().Select(t => t.GetInt32())];

    /// <summary>
    /// Action acceptée : 204, ou 202 et sa tâche de fond, suivie par <c>/api/taches</c> jusqu'à son
    /// succès. Rend la réponse reçue, pour les messages d'échec.
    /// </summary>
    private async Task<string> WaitForBulkAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var description = $"HTTP {(int)response.StatusCode} {body}";
        Assert.True(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Accepted, description);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return description;
        }

        var task = JsonElement.Parse(body).GetProperty("tache").GetGuid();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var state = await app.GetJsonAsync($"/api/taches/{task:D}");
            var status = state.GetProperty("status").GetString();
            if (status == "success")
            {
                return description;
            }

            Assert.True(status is "pending" or "started", $"Tâche {task} : {status} ({state.GetProperty("message")})");
            await Task.Delay(500);
        }

        Assert.Fail($"La tâche {task} n'est pas terminée après 30 s.");
        return description;
    }

    /// <summary>L'action en masse peut finir juste après la tâche : quelques relectures du document.</summary>
    private static async Task EventuallyAsync(Func<Task<bool>> condition, string what, string bulkResponse)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(500);
        }

        Assert.Fail($"{what} : non constaté après 10 s (réponse de l'action : {bulkResponse}).");
    }

    [Fact]
    public async Task Ajout_d_un_document_puis_suivi_de_sa_consommation()
    {
        using var form = HomeGedAppFixture.TextFile("document", $"ajout-{app.Marker}.txt", $"Ajout depuis Home-GED {app.Marker} {Guid.NewGuid():N}");
        using var upload = await app.SendAsync(HttpMethod.Post, "/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var task = JsonElement.Parse(await upload.Content.ReadAsStringAsync()).GetProperty("tache").GetGuid();

        var created = await app.WaitForDocumentAsync(task);
        try
        {
            var document = await app.GetJsonAsync($"/api/documents/{created}");
            Assert.Contains("Ajout depuis Home-GED", document.GetProperty("content").GetString(), StringComparison.Ordinal);
            var state = await app.GetJsonAsync($"/api/taches/{task:D}");
            Assert.Equal("success", state.GetProperty("status").GetString());
            Assert.Equal(created, state.GetProperty("document_id").GetInt32());
        }
        finally
        {
            using var _ = await app.SendAsync(HttpMethod.Delete, $"/api/documents/{created}");
        }
    }
}
