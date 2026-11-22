using System.Net;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Data;
using HomeGed.Api.Shares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HomeGed.Api.Tests;

/// <summary>
/// Page publique des liens (SHARE-03), journal (SHARE-05) et limitation des tentatives (SHARE-06).
/// Le faux Paperless sert le document 40 (« Bail ») au compte de service ; le 41 lui est invisible.
/// </summary>
public sealed class PublicShareEndpointsTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
{
    private const string Password = "Correct-Cheval-42";

    /// <summary>Empreinte calculée une fois : Argon2id coûte ~20 Mio et quelques dizaines de ms.</summary>
    private static readonly Lazy<string> Hash = new(() => SharePasswordHasher.Hash(Password));

    private static int _nextIp;

    private readonly string _ip = $"203.0.113.{Interlocked.Increment(ref _nextIp)}";

    private ShareLink AddLink(
        int document = 40, int owner = 1, int? maxDownloads = null, int downloads = 0, TimeSpan? lifetime = null, bool revoked = false)
    {
        var now = fixture.Time.GetUtcNow();
        var link = new ShareLink
        {
            Id = ShareLinkIds.New(),
            DocumentId = document,
            OwnerId = owner,
            PasswordHash = Hash.Value,
            CreatedAt = now.AddDays(-1),
            ExpiresAt = now + (lifetime ?? TimeSpan.FromDays(7)),
            MaxDownloads = maxDownloads,
            DownloadCount = downloads,
            RevokedAt = revoked ? now : null,
        };
        fixture.Shares.Links[link.Id] = link;
        return link;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string? json = null, string? ticket = null, string? ip = null, bool antiForgery = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip ?? _ip);
        request.Headers.TryAddWithoutValidation("User-Agent", "Navigateur-Test/1.0");
        if (antiForgery)
        {
            request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        }

        if (ticket is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{ShareTickets.CookieName}={ticket}");
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        return await fixture.Http.SendAsync(request);
    }

    private Task<HttpResponseMessage> AccessAsync(string id, string password = Password, string? ip = null) =>
        SendAsync(HttpMethod.Post, $"/api/public/partages/{id}/acces", $$"""{"motDePasse":"{{password}}"}""", ip: ip);

    private static string? TicketCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(h => h.StartsWith(ShareTickets.CookieName + "=", StringComparison.Ordinal))
            : null;

    private async Task<string> TicketAsync(string id)
    {
        using var response = await AccessAsync(id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return TicketCookie(response)!.Split(';')[0][(ShareTickets.CookieName.Length + 1)..];
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())?["code"]?.GetValue<string>();

    [Fact]
    public async Task Bon_mot_de_passe_delivre_un_ticket_restreint_au_lien()
    {
        var link = AddLink(maxDownloads: 3, downloads: 1);

        using var response = await AccessAsync(link.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("noindex", string.Join(',', response.Headers.GetValues("X-Robots-Tag")), StringComparison.Ordinal);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("Bail", body["titre"]!.GetValue<string>());
        Assert.Equal(2, body["telechargementsRestants"]!.GetValue<int>());
        Assert.Null(body["document"]);

        var cookie = TicketCookie(response)!.ToUpperInvariant();
        Assert.Contains("HTTPONLY", cookie, StringComparison.Ordinal);
        Assert.Contains("SECURE", cookie, StringComparison.Ordinal);
        Assert.Contains("SAMESITE=STRICT", cookie, StringComparison.Ordinal);
        Assert.Contains($"PATH=/API/PUBLIC/PARTAGES/{link.Id.ToUpperInvariant()}", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain(Password.ToUpperInvariant(), cookie, StringComparison.Ordinal);

        var entry = Assert.Single(fixture.Shares.AccessLog, a => a.ShareLinkId == link.Id);
        Assert.True(entry.Success);
        Assert.Equal(_ip, entry.Ip);
        Assert.Equal("Navigateur-Test/1.0", entry.UserAgent);
    }

    [Fact]
    public async Task Tous_les_refus_sont_identiques()
    {
        var wrong = AddLink();
        var expired = AddLink(lifetime: TimeSpan.FromSeconds(-1));
        var revoked = AddLink(revoked: true);
        var exhausted = AddLink(maxDownloads: 2, downloads: 2);
        var invisible = AddLink(document: 41);
        string?[] bodies =
        [
            await RefusalAsync(wrong.Id, "Mauvais-Mot-De-Passe"),
            await RefusalAsync(ShareLinkIds.New()),
            await RefusalAsync("pas-un-identifiant"),
            await RefusalAsync(expired.Id),
            await RefusalAsync(revoked.Id),
            await RefusalAsync(exhausted.Id),
            await RefusalAsync(invisible.Id),
            await RefusalAsync(wrong.Id, ""),
        ];

        Assert.Single(bodies.Distinct());
        Assert.Contains(PublicShareEndpoints.UnavailableCode, bodies[0], StringComparison.Ordinal);

        async Task<string?> RefusalAsync(string id, string password = Password)
        {
            // IP propre à chaque essai : on ne teste ici que la réponse, pas le blocage.
            using var response = await AccessAsync(id, password, ip: $"192.0.2.{Interlocked.Increment(ref _nextIp)}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(TicketCookie(response));
            return await response.Content.ReadAsStringAsync();
        }
    }

    [Fact]
    public async Task Echec_journalise_sans_le_mot_de_passe()
    {
        var link = AddLink();

        using var response = await AccessAsync(link.Id, "Mauvais-Mot-De-Passe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var entry = Assert.Single(fixture.Shares.AccessLog, a => a.ShareLinkId == link.Id);
        Assert.False(entry.Success);
        Assert.Equal(_ip, entry.Ip);
        Assert.Equal(fixture.Time.GetUtcNow(), entry.Timestamp);
    }

    [Fact]
    public async Task Cinq_echecs_bloquent_le_lien_quinze_minutes_meme_depuis_une_autre_ip()
    {
        var link = AddLink();
        for (var i = 0; i < 5; i++)
        {
            using var failure = await AccessAsync(link.Id, "Mauvais-Mot-De-Passe", ip: $"192.0.2.{200 + i}");
            Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        }

        var logged = fixture.Shares.AccessLog.Count(a => a.ShareLinkId == link.Id);
        using var blocked = await AccessAsync(link.Id);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("trop_de_tentatives", await ErrorCodeAsync(blocked));
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Null(TicketCookie(blocked));
        // Bloqué avant toute vérification : rien de plus au journal.
        Assert.Equal(logged, fixture.Shares.AccessLog.Count(a => a.ShareLinkId == link.Id));

        fixture.Time.Advance(TimeSpan.FromMinutes(15));
        using var later = await AccessAsync(link.Id);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
    }

    [Fact]
    public async Task Cinq_echecs_bloquent_l_ip_sur_tous_les_liens()
    {
        for (var i = 0; i < 5; i++)
        {
            using var failure = await AccessAsync(ShareLinkIds.New());
            Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        }

        var link = AddLink();
        using var blocked = await AccessAsync(link.Id);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        using var otherIp = await AccessAsync(link.Id, ip: "192.0.2.250");
        Assert.Equal(HttpStatusCode.OK, otherIp.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/apercu")]
    [InlineData("/telechargement")]
    public async Task Sans_ticket_le_mot_de_passe_est_demande(string suffix)
    {
        var link = AddLink();
        var other = AddLink();
        var otherTicket = await TicketAsync(other.Id);

        using var none = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}{suffix}");
        using var forged = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}{suffix}", ticket: "faux");
        using var foreign = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}{suffix}", ticket: otherTicket);

        foreach (var response in new[] { none, forged, foreign })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("mot_de_passe_requis", await ErrorCodeAsync(response));
        }

        Assert.Equal(0, fixture.Shares.Links[link.Id].DownloadCount);
    }

    [Fact]
    public async Task Avec_ticket_infos_et_apercu_sans_compter_de_telechargement()
    {
        var link = AddLink(maxDownloads: 1);
        var ticket = await TicketAsync(link.Id);

        using var info = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}", ticket: ticket);
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.Equal("Bail", JsonNode.Parse(await info.Content.ReadAsStringAsync())!["titre"]!.GetValue<string>());

        using var preview = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}/apercu", ticket: ticket);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("application/pdf", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal("SAMEORIGIN", string.Join(',', preview.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-store", preview.Headers.CacheControl?.ToString());
        Assert.Equal(0, fixture.Shares.Links[link.Id].DownloadCount);
        Assert.Contains(fixture.Paperless.History, c => c.PathAndQuery == "/api/documents/40/preview/");
    }

    [Fact]
    public async Task Telechargement_compte_et_respecte_le_quota()
    {
        var link = AddLink(maxDownloads: 1);
        var ticket = await TicketAsync(link.Id);

        using var first = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}/telechargement", ticket: ticket);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("%PDF-1.7 bail", await first.Content.ReadAsStringAsync());
        Assert.Equal("attachment", first.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(1, fixture.Shares.Links[link.Id].DownloadCount);

        using var second = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}/telechargement", ticket: ticket);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(PublicShareEndpoints.UnavailableCode, await ErrorCodeAsync(second));
        Assert.Equal(1, fixture.Shares.Links[link.Id].DownloadCount);
    }

    [Fact]
    public async Task Telechargements_simultanes_ne_depassent_pas_le_quota()
    {
        var link = AddLink(maxDownloads: 2);
        var ticket = await TicketAsync(link.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}/telechargement", ticket: ticket)));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(2, fixture.Shares.Links[link.Id].DownloadCount);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Lien_revoque_apres_le_ticket_refuse()
    {
        var link = AddLink();
        var ticket = await TicketAsync(link.Id);
        fixture.Shares.Links[link.Id].RevokedAt = fixture.Time.GetUtcNow();

        using var response = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}/telechargement", ticket: ticket);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(PublicShareEndpoints.UnavailableCode, await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Ticket_valable_trente_minutes()
    {
        var link = AddLink();
        var ticket = await TicketAsync(link.Id);

        fixture.Time.Advance(ShareTickets.Lifetime);
        using var response = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}", ticket: ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Acces_sans_en_tete_anti_forgery_refuse()
    {
        var link = AddLink();

        using var response = await SendAsync(
            HttpMethod.Post, $"/api/public/partages/{link.Id}/acces", $$"""{"motDePasse":"{{Password}}"}""", antiForgery: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(fixture.Shares.AccessLog, a => a.ShareLinkId == link.Id);
    }

    [Fact]
    public async Task Le_fichier_est_lu_avec_le_compte_de_service()
    {
        var link = AddLink();
        var ticket = await TicketAsync(link.Id);
        var before = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Get, $"/api/public/partages/{link.Id}/telechargement", ticket: ticket);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(fixture.Paperless.History.Skip(before), c => c.PathAndQuery == "/api/documents/40/download/");
    }
}

/// <summary>Journal d'accès vu par le propriétaire du lien (SHARE-05).</summary>
public sealed class ShareAccessLogEndpointTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
{
    private async Task<HttpResponseMessage> GetAsync(string path, string? user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.250");
        if (user is not null)
        {
            using var login = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/login", UriKind.Relative))
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { username = user, password = "secret", code = user == "bob" ? "123456" : null }),
            };
            login.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
            using var logged = await fixture.Http.SendAsync(login);
            var cookie = logged.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(SessionTokens.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return await fixture.Http.SendAsync(request);
    }

    [Fact]
    public async Task Le_proprietaire_voit_le_journal_le_plus_recent_d_abord()
    {
        var now = fixture.Time.GetUtcNow();
        var link = new ShareLink { Id = ShareLinkIds.New(), DocumentId = 40, OwnerId = 1, ExpiresAt = now.AddDays(1) };
        fixture.Shares.Links[link.Id] = link;
        await fixture.Shares.LogAccessAsync(
            new ShareAccessLog { ShareLinkId = link.Id, Timestamp = now.AddMinutes(-2), Ip = "192.0.2.1", Success = false }, default);
        await fixture.Shares.LogAccessAsync(
            new ShareAccessLog { ShareLinkId = link.Id, Timestamp = now, Ip = "192.0.2.2", UserAgent = "Firefox", Success = true }, default);

        using var response = await GetAsync($"/api/partages/{link.Id}/journal", "alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var entries = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["results"]!.AsArray();
        Assert.Equal(2, entries.Count);
        Assert.Equal("192.0.2.2", entries[0]!["ip"]!.GetValue<string>());
        Assert.Equal("Firefox", entries[0]!["navigateur"]!.GetValue<string>());
        Assert.True(entries[0]!["succes"]!.GetValue<bool>());
        Assert.False(entries[1]!["succes"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("bob")]
    [InlineData(null)]
    public async Task Journal_refuse_a_un_autre_utilisateur_ou_sans_session(string? user)
    {
        var link = new ShareLink { Id = ShareLinkIds.New(), DocumentId = 40, OwnerId = 1, ExpiresAt = fixture.Time.GetUtcNow().AddDays(1) };
        fixture.Shares.Links[link.Id] = link;

        using var response = await GetAsync($"/api/partages/{link.Id}/journal", user);

        Assert.Equal(user is null ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>Sans token de compte de service : les liens ne s'ouvrent pas, sans erreur 500.</summary>
public sealed class PublicShareWithoutServiceTokenTests : IAsyncLifetime
{
    private readonly InMemoryShareLinkStore _shares = new();

    private WebApplication? _app;

    private Uri _address = null!;

    public async Task InitializeAsync()
    {
        _app = HomeGedApp.Create(
            ["--Paperless:BaseUrl=http://paperless:8000", "--Database:ConnectionString=Host=127.0.0.1;Port=9", "--Database:MigrateOnStartup=false"],
            b =>
            {
                b.WebHost.UseUrls("http://127.0.0.1:0");
                b.Services.AddHttpClient<Paperless.PaperlessClient>().ConfigurePrimaryHttpMessageHandler(
                    () => new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
                b.Services.AddSingleton<IShareLinkStore>(_shares);
                b.Services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(
                    new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
            });
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _address = new Uri(address);
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Partage_indisponible_503()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/public/partages/{ShareLinkIds.New()}/acces", UriKind.Relative))
        {
            Content = new StringContent("""{"motDePasse":"x"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");

        using var http = new HttpClient { BaseAddress = _address };
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("partage_indisponible", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
