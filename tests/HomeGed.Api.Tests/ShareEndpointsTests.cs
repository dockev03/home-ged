using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Shares;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeGed.Api.Tests;

/// <summary>
/// Routes du faux Paperless pour les liens de partage : alice voit le document 40 (« Bail »),
/// bob ne le voit pas ; le document 41 n'est visible de personne.
/// </summary>
internal static class FakePaperlessShares
{
    public static HttpResponseMessage? Respond(HttpRequestMessage request, string? body)
    {
        _ = body;
        var path = request.RequestUri!.AbsolutePath;
        var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
        var token = request.Headers.Authorization?.Parameter;
        if (path is "/api/documents/40/" or "/api/documents/41/")
        {
            return path == "/api/documents/40/" && token == "tok-alice"
                ? FakePaperless.Json(HttpStatusCode.OK, """{"id":40}""")
                : FakePaperless.Json(HttpStatusCode.NotFound, """{"detail":"No Document matches the given query."}""");
        }

        if (path == "/api/documents/" && query.ContainsKey("id__in"))
        {
            return token == "tok-alice"
                ? FakePaperless.Json(HttpStatusCode.OK, """{"count":1,"results":[{"id":40,"title":"Bail","content":"secret"}]}""")
                : FakePaperless.Json(HttpStatusCode.OK, """{"count":0,"results":[]}""");
        }

        return null;
    }
}

public sealed class ShareEndpointsTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
{
    private static int _nextIp;

    private readonly string _ip = $"198.51.100.{100 + Interlocked.Increment(ref _nextIp)}";

    private async Task<string> LoginAsync(string username = "alice")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new { username, password = "secret", code = username == "bob" ? "123456" : null }),
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", _ip);
        request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        using var response = await fixture.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var header = response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(SessionTokens.CookieName + "=", StringComparison.Ordinal));
        return header.Split(';')[0][(SessionTokens.CookieName.Length + 1)..];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, string? json = null, bool antiForgery = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", _ip);
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{SessionTokens.CookieName}={cookie}");
        }

        if (antiForgery)
        {
            request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        return await fixture.Http.SendAsync(request);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<JsonNode> CreateAsync(string cookie, string json = """{"document":40,"motDePasse":"Correct-Cheval-42","jours":7}""")
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/partages", cookie, json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    [Theory]
    [InlineData("GET", "/api/partages")]
    [InlineData("GET", "/api/partages/reglages")]
    [InlineData("POST", "/api/partages")]
    [InlineData("DELETE", "/api/partages/AAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Sans_session_401(string method, string path)
    {
        using var response = await SendAsync(new HttpMethod(method), path, null, method == "POST" ? "{}" : null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creation_sans_en_tete_anti_forgery_refusee()
    {
        var cookie = await LoginAsync();
        var before = fixture.Shares.Links.Count;

        using var response = await SendAsync(
            HttpMethod.Post, "/api/partages", cookie, """{"document":40,"motDePasse":"Correct-Cheval-42","jours":7}""", antiForgery: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, fixture.Shares.Links.Count);
    }

    [Fact]
    public async Task Reglages_donnent_la_duree_maximale()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Get, "/api/partages/reglages", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(30, JsonNode.Parse(await response.Content.ReadAsStringAsync())!["joursMax"]!.GetValue<int>());
    }

    [Fact]
    public async Task Creation_stocke_une_empreinte_argon2id_et_jamais_le_mot_de_passe()
    {
        var cookie = await LoginAsync();

        var created = await CreateAsync(cookie, """{"document":40,"motDePasse":"Correct-Cheval-42","jours":7,"telechargementsMax":3}""");

        var id = created["id"]!.GetValue<string>();
        Assert.True(ShareLinkIds.IsValid(id));
        Assert.Equal(40, created["document"]!.GetValue<int>());
        Assert.Equal(3, created["telechargementsMax"]!.GetValue<int>());
        Assert.Equal(0, created["telechargements"]!.GetValue<int>());
        Assert.DoesNotContain("Correct-Cheval-42", created.ToJsonString(), StringComparison.Ordinal);
        Assert.Null(created["motDePasse"]);

        var stored = fixture.Shares.Links[id];
        Assert.Equal(1, stored.OwnerId);
        Assert.StartsWith("$argon2id$", stored.PasswordHash, StringComparison.Ordinal);
        Assert.True(SharePasswordHasher.Verify("Correct-Cheval-42", stored.PasswordHash));
        Assert.Equal(TimeSpan.FromDays(7), stored.ExpiresAt - stored.CreatedAt);
        Assert.Equal(3, stored.MaxDownloads);
    }

    [Fact]
    public async Task Sans_limite_de_telechargements_par_defaut()
    {
        var cookie = await LoginAsync();

        var created = await CreateAsync(cookie);

        Assert.Null(created["telechargementsMax"]);
        Assert.Null(fixture.Shares.Links[created["id"]!.GetValue<string>()].MaxDownloads);
    }

    [Theory]
    [InlineData("""{"motDePasse":"Correct-Cheval-42","jours":7}""")]
    [InlineData("""{"document":"40","motDePasse":"Correct-Cheval-42","jours":7}""")]
    [InlineData("""{"document":-1,"motDePasse":"Correct-Cheval-42","jours":7}""")]
    [InlineData("""{"document":40,"jours":7}""")]
    [InlineData("""{"document":40,"motDePasse":"","jours":7}""")]
    [InlineData("""{"document":40,"motDePasse":"court","jours":7}""")]
    [InlineData("""{"document":40,"motDePasse":"Correct-Cheval-42"}""")]
    [InlineData("""{"document":40,"motDePasse":"Correct-Cheval-42","jours":0}""")]
    [InlineData("""{"document":40,"motDePasse":"Correct-Cheval-42","jours":31}""")]
    [InlineData("""{"document":40,"motDePasse":"Correct-Cheval-42","jours":7.5}""")]
    [InlineData("""{"document":40,"motDePasse":"Correct-Cheval-42","jours":7,"telechargementsMax":0}""")]
    [InlineData("""{"document":40,"motDePasse":"Correct-Cheval-42","jours":7,"telechargementsMax":1001}""")]
    public async Task Requete_invalide_400_sans_appel_a_Paperless(string json)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.History.Count;
        var links = fixture.Shares.Links.Count;

        using var response = await SendAsync(HttpMethod.Post, "/api/partages", cookie, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("requete_invalide", await ErrorCodeAsync(response));
        Assert.DoesNotContain(fixture.Paperless.History.Skip(calls), c => c.PathAndQuery.StartsWith("/api/documents/", StringComparison.Ordinal));
        Assert.Equal(links, fixture.Shares.Links.Count);
    }

    [Fact]
    public async Task Mot_de_passe_trop_long_refuse()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(
            HttpMethod.Post, "/api/partages", cookie, $$"""{"document":40,"motDePasse":"{{new string('x', 129)}}","jours":7}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("alice", 41)]
    [InlineData("bob", 40)]
    public async Task Document_que_l_utilisateur_ne_voit_pas_404_et_aucun_lien(string user, int document)
    {
        var cookie = await LoginAsync(user);
        var links = fixture.Shares.Links.Count;
        var calls = fixture.Paperless.History.Count;

        using var response = await SendAsync(
            HttpMethod.Post, "/api/partages", cookie, $$"""{"document":{{document}},"motDePasse":"Correct-Cheval-42","jours":7}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("introuvable", await ErrorCodeAsync(response));
        Assert.Equal(links, fixture.Shares.Links.Count);
        Assert.Contains(fixture.Paperless.History.Skip(calls), c => c.PathAndQuery == $"/api/documents/{document}/?fields=id");
    }

    [Fact]
    public async Task Liste_ses_liens_actifs_avec_le_titre_sans_contenu()
    {
        var alice = await LoginAsync();
        var created = await CreateAsync(alice);
        var id = created["id"]!.GetValue<string>();

        using var response = await SendAsync(HttpMethod.Get, "/api/partages", alice);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("argon2", body, StringComparison.Ordinal);
        var link = JsonNode.Parse(body)!["results"]!.AsArray().Single(l => l!["id"]!.GetValue<string>() == id)!;
        Assert.Equal("Bail", link["titre"]!.GetValue<string>());
        Assert.Contains(fixture.Paperless.History, c => c.PathAndQuery == "/api/documents/?id__in=40&fields=id,title&page_size=1");
    }

    [Fact]
    public async Task Les_liens_d_un_autre_utilisateur_ne_sont_ni_listes_ni_revocables()
    {
        var alice = await LoginAsync();
        var id = (await CreateAsync(alice))["id"]!.GetValue<string>();
        var bob = await LoginAsync("bob");

        using var list = await SendAsync(HttpMethod.Get, "/api/partages", bob);
        Assert.DoesNotContain(id, await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var revoke = await SendAsync(HttpMethod.Delete, $"/api/partages/{id}", bob);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        Assert.Null(fixture.Shares.Links[id].RevokedAt);
    }

    [Fact]
    public async Task Revocation_en_un_clic_puis_absent_de_la_liste()
    {
        var alice = await LoginAsync();
        var id = (await CreateAsync(alice))["id"]!.GetValue<string>();

        using var revoke = await SendAsync(HttpMethod.Delete, $"/api/partages/{id}", alice);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.NotNull(fixture.Shares.Links[id].RevokedAt);

        using var again = await SendAsync(HttpMethod.Delete, $"/api/partages/{id}", alice);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        using var list = await SendAsync(HttpMethod.Get, "/api/partages", alice);
        Assert.DoesNotContain(id, await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("inconnu")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Revocation_d_un_lien_inconnu_404(string id)
    {
        var alice = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Delete, $"/api/partages/{id}", alice);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("introuvable", await ErrorCodeAsync(response));
    }
}
