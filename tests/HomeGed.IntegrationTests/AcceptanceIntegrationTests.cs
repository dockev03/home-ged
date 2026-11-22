using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeGed.Api.Auth;
using HomeGed.Api.Data;
using HomeGed.Api.Paperless;
using HomeGed.Api.Shares;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeGed.IntegrationTests;

/// <summary>
/// Critère d'acceptation du §7 : « un utilisateur ne voit jamais un document qui ne lui est pas
/// partagé dans Paperless ». Un second utilisateur, ordinaire, est créé dans un Paperless réel.
/// </summary>
[Collection(BaseHomeGed.Name)]
public sealed class CloisonnementIntegrationTests(HomeGedAppFixture app) : IClassFixture<HomeGedAppFixture>, IAsyncLifetime
{
    private int _otherId;

    private string _other = "";

    public async Task InitializeAsync()
    {
        var username = "autre-" + app.Marker;
        var password = Guid.NewGuid().ToString("N");
        // view_uisettings : sans elle, Paperless refuse /api/ui_settings/ et la connexion à Home-GED échoue.
        _otherId = await app.CreatePaperlessUserAsync(
            username, password, "view_uisettings", "view_document", "add_document", "change_document", "delete_document", "add_note", "view_note");
        (_other, _) = await app.LoginAsync(username, password);
    }

    public Task DisposeAsync() => app.DeletePaperlessUserAsync(_otherId);

    [Fact]
    public async Task Un_utilisateur_ne_voit_que_les_documents_partages_avec_lui_dans_Paperless()
    {
        var id = app.DocumentId;

        // Ni par la liste, ni par la recherche plein texte, ni par la sélection « tout ».
        Assert.Equal(0, (await GetAsOtherAsync($"/api/documents?title_content={app.Marker}")).GetProperty("count").GetInt32());
        Assert.Equal(0, (await GetAsOtherAsync($"/api/documents?query={app.Marker}")).GetProperty("count").GetInt32());
        Assert.Equal(0, (await GetAsOtherAsync($"/api/documents/ids?title_content={app.Marker}")).GetArrayLength());

        // Ni en accès direct par l'identifiant, en lecture comme en écriture.
        foreach (var path in new[] { "", "/preview", "/download", "/thumb", "/metadata", "/notes", "/history", "/permissions" })
        {
            await AssertRefusedAsync(HttpMethod.Get, $"/api/documents/{id}{path}");
        }

        await AssertRefusedAsync(HttpMethod.Patch, $"/api/documents/{id}", new { title = "Piraté" });
        await AssertRefusedAsync(HttpMethod.Post, $"/api/documents/{id}/notes", new { note = "Piraté" });
        await AssertRefusedAsync(HttpMethod.Delete, $"/api/documents/{id}");
        await AssertRefusedAsync(HttpMethod.Post, "/api/partages", new { document = id, motDePasse = "mot-de-passe-long", jours = 1 });

        // Le propriétaire le partage dans Paperless : il devient visible, ce qui prouve que le refus venait des droits.
        using var shared = await app.SendAsync(HttpMethod.Patch, $"/api/documents/{id}/permissions", new
        {
            owner = app.UserId,
            set_permissions = new
            {
                view = new { users = new[] { _otherId }, groups = Array.Empty<int>() },
                change = new { users = Array.Empty<int>(), groups = Array.Empty<int>() },
            },
        });
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);

        Assert.Equal(1, (await GetAsOtherAsync($"/api/documents?title_content={app.Marker}")).GetProperty("count").GetInt32());
        Assert.Contains(app.Marker, (await GetAsOtherAsync($"/api/documents/{id}")).GetProperty("title").GetString(), StringComparison.Ordinal);

        // Lecture seule : la modification reste refusée.
        await AssertRefusedAsync(HttpMethod.Patch, $"/api/documents/{id}", new { title = "Piraté" });
        Assert.DoesNotContain("Piraté", (await app.GetJsonAsync($"/api/documents/{id}")).GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    private async Task<JsonElement> GetAsOtherAsync(string path)
    {
        using var response = await app.SendAsync(HttpMethod.Get, path, cookie: _other);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path} : HTTP {(int)response.StatusCode} {text}");
        return JsonElement.Parse(text);
    }

    private async Task AssertRefusedAsync(HttpMethod method, string path, object? body = null)
    {
        using var response = await app.SendAsync(method, path, body, _other);
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"{method} {path} : HTTP {(int)response.StatusCode} au lieu d'un refus");
    }
}

/// <summary>Home-GED démarré avec un vrai compte de service Paperless en lecture seule pour les liens de partage.</summary>
public sealed class ShareAppFixture : HomeGedAppFixture
{
    public int ServiceUserId { get; private set; }

    protected override async Task<IEnumerable<string>> AdditionalArgumentsAsync()
    {
        var username = "partage-" + Marker;
        var password = Guid.NewGuid().ToString("N");
        ServiceUserId = await CreatePaperlessUserAsync(username, password, "view_document");

        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        PaperlessClient.Configure(http, new PaperlessOptions { BaseUrl = IntegrationEnvironment.PaperlessUrl });
        var token = await new PaperlessClient(http, NullLogger<PaperlessClient>.Instance)
            .RequestTokenAsync(username, password, null, "198.51.100.20", CancellationToken.None);
        Assert.Equal(TokenOutcome.Success, token.Outcome);
        return [$"--Share:ServiceToken={token.Token}"];
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // Le compte de service doit voir le document partagé (droit de lecture accordé dans Paperless).
        using var granted = await Paperless.PatchAsync(new Uri($"api/documents/{DocumentId}/", UriKind.Relative), Json(new
        {
            set_permissions = new
            {
                view = new { users = new[] { ServiceUserId }, groups = Array.Empty<int>() },
                change = new { users = Array.Empty<int>(), groups = Array.Empty<int>() },
            },
        }));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await DeletePaperlessUserAsync(ServiceUserId);
    }
}

/// <summary>
/// Critère d'acceptation du §7 sur les liens de partage, de bout en bout (Paperless et PostgreSQL réels) :
/// sans mot de passe → refus ; 5 mauvais mots de passe → blocage ; lien expiré ou révoqué → refus ;
/// chaque accès est journalisé. Chaque test parle depuis sa propre IP (X-Forwarded-For) pour que
/// les blocages par IP de l'un ne gênent pas les autres.
/// </summary>
[Collection(BaseHomeGed.Name)]
public sealed class PartageIntegrationTests(ShareAppFixture app) : IClassFixture<ShareAppFixture>
{
    private const string Password = "cheval-correct-42";

    [Fact]
    public async Task Mot_de_passe_obligatoire_puis_apercu_telechargement_et_journal()
    {
        const string ip = "198.51.100.31";
        var id = await CreateLinkAsync(maxDownloads: 1);

        using (var noTicket = await PublicAsync(HttpMethod.Get, id, "", ip))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, noTicket.StatusCode);
        }

        using (var noPassword = await PublicAsync(HttpMethod.Post, id, "/acces", ip, body: new { }))
        {
            await AssertUnavailableAsync(noPassword);
        }

        var ticket = await AccessAsync(id, ip);

        using (var preview = await PublicAsync(HttpMethod.Get, id, "/apercu", ip, ticket))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Contains(app.Marker, await preview.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var download = await PublicAsync(HttpMethod.Get, id, "/telechargement", ip, ticket))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Contains(app.Marker, await download.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // Quota d'un téléchargement atteint : le lien n'est plus utilisable.
        using (var again = await PublicAsync(HttpMethod.Get, id, "/telechargement", ip, ticket))
        {
            await AssertUnavailableAsync(again);
        }

        var log = await JournalAsync(id);
        Assert.Equal([true, false], log.Select(e => e.GetProperty("succes").GetBoolean()));
        Assert.All(log, e => Assert.Equal(ip, e.GetProperty("ip").GetString()));
    }

    [Fact]
    public async Task Cinq_mauvais_mots_de_passe_bloquent_le_lien()
    {
        var id = await CreateLinkAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await PublicAsync(HttpMethod.Post, id, "/acces", "198.51.100.41", body: new { motDePasse = "mauvais-" + attempt });
            await AssertUnavailableAsync(wrong);
        }

        // Bloqué pour le lien lui-même : même le bon mot de passe, depuis une autre IP, est refusé.
        using (var blocked = await PublicAsync(HttpMethod.Post, id, "/acces", "198.51.100.42", body: new { motDePasse = Password }))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            Assert.True(blocked.Headers.RetryAfter is not null, "Retry-After attendu");
        }

        var log = await JournalAsync(id);
        Assert.Equal(5, log.Count);
        Assert.All(log, e => Assert.False(e.GetProperty("succes").GetBoolean()));
    }

    [Fact]
    public async Task Lien_revoque_refuse_meme_avec_le_bon_mot_de_passe_ou_un_ticket()
    {
        const string ip = "198.51.100.51";
        var id = await CreateLinkAsync();
        var ticket = await AccessAsync(id, ip);

        using (var revoked = await app.SendAsync(HttpMethod.Delete, $"/api/partages/{id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }

        using (var withTicket = await PublicAsync(HttpMethod.Get, id, "/telechargement", ip, ticket))
        {
            await AssertUnavailableAsync(withTicket);
        }

        using (var withPassword = await PublicAsync(HttpMethod.Post, id, "/acces", ip, body: new { motDePasse = Password }))
        {
            await AssertUnavailableAsync(withPassword);
        }

        // La tentative après révocation est journalisée comme un échec.
        Assert.False((await JournalAsync(id))[0].GetProperty("succes").GetBoolean());
    }

    [Fact]
    public async Task Lien_expire_refuse_avec_le_bon_mot_de_passe()
    {
        // Une durée minimale d'un jour est imposée à la création : le lien expiré est écrit directement en base.
        var id = ShareLinkIds.New();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var now = DateTimeOffset.UtcNow;
            await scope.ServiceProvider.GetRequiredService<IShareLinkStore>().AddAsync(
                new ShareLink
                {
                    Id = id,
                    DocumentId = app.DocumentId,
                    OwnerId = app.UserId,
                    PasswordHash = SharePasswordHasher.Hash(Password),
                    CreatedAt = now.AddDays(-2),
                    ExpiresAt = now.AddMinutes(-1),
                },
                CancellationToken.None);
        }

        using (var expired = await PublicAsync(HttpMethod.Post, id, "/acces", "198.51.100.61", body: new { motDePasse = Password }))
        {
            await AssertUnavailableAsync(expired);
            Assert.False(expired.Headers.TryGetValues("Set-Cookie", out _), "Aucun ticket ne doit être délivré");
        }

        var log = await JournalAsync(id);
        Assert.False(Assert.Single(log).GetProperty("succes").GetBoolean());
    }

    private async Task<string> CreateLinkAsync(int? maxDownloads = null)
    {
        using var created = await app.SendAsync(HttpMethod.Post, "/api/partages", new
        {
            document = app.DocumentId,
            motDePasse = Password,
            jours = 1,
            telechargementsMax = maxDownloads,
        });
        var text = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"Création du lien : HTTP {(int)created.StatusCode} {text}");
        return JsonElement.Parse(text).GetProperty("id").GetString()!;
    }

    /// <summary>Bon mot de passe : renvoie le ticket d'accès (valeur du cookie limité au lien).</summary>
    private async Task<string> AccessAsync(string id, string ip)
    {
        using var access = await PublicAsync(HttpMethod.Post, id, "/acces", ip, body: new { motDePasse = Password });
        var text = await access.Content.ReadAsStringAsync();
        Assert.True(access.StatusCode == HttpStatusCode.OK, $"Accès au lien : HTTP {(int)access.StatusCode} {text}");
        Assert.Contains(app.Marker, JsonElement.Parse(text).GetProperty("titre").GetString(), StringComparison.Ordinal);
        return access.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith(ShareTickets.CookieName + "=", StringComparison.Ordinal))
            .Split(';')[0][(ShareTickets.CookieName.Length + 1)..];
    }

    /// <summary>Requête anonyme à la page publique, depuis l'IP donnée (transmise comme par NPM).</summary>
    private async Task<HttpResponseMessage> PublicAsync(
        HttpMethod method, string id, string suffix, string ip, string? ticket = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri($"/api/public/partages/{id}{suffix}", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);
        request.Headers.UserAgent.ParseAdd("HomeGed-Tests/1.0");
        if (ticket is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{ShareTickets.CookieName}={ticket}");
        }

        request.Content = body is null ? null : JsonContent.Create(body);
        return await app.Http.SendAsync(request);
    }

    private async Task<List<JsonElement>> JournalAsync(string id) =>
        [.. (await app.GetJsonAsync($"/api/partages/{id}/journal")).GetProperty("results").EnumerateArray()];

    private static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"HTTP {(int)response.StatusCode} {text}");
        Assert.Contains(PublicShareEndpoints.UnavailableCode, text, StringComparison.Ordinal);
    }
}
