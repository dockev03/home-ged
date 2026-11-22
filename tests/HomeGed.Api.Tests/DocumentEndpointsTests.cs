using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeGed.Api.Auth;
using HomeGed.Api.Documents;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace HomeGed.Api.Tests;

/// <summary>Routes documents et listes de référence du faux Paperless (tokens tok-alice et tok-bob).</summary>
internal static class FakePaperlessDocuments
{
    public static HttpResponseMessage? Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith("/api/documents/", StringComparison.Ordinal)
            && !path.StartsWith("/api/tags/", StringComparison.Ordinal)
            && !path.StartsWith("/api/correspondents/", StringComparison.Ordinal))
        {
            return null;
        }

        var token = request.Headers.Authorization?.Parameter;
        if (token is not ("tok-alice" or "tok-bob"))
        {
            return FakePaperless.Json(HttpStatusCode.Unauthorized, """{"detail":"Invalid token."}""");
        }

        var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
        var ordering = query.TryGetValue("ordering", out var o) ? o.ToString() : null;
        var page = query.TryGetValue("page", out var p) ? p.ToString() : null;
        switch (path)
        {
            case "/api/documents/" when ordering == "inconnu":
                return FakePaperless.Json(HttpStatusCode.BadRequest, """{"ordering":["invalide"]}""");

            case "/api/documents/" when token == "tok-bob":
                return FakePaperless.Json(HttpStatusCode.Forbidden, """{"detail":"Forbidden"}""");

            case "/api/documents/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":30,"next":"http://paperless:8000/api/documents/?page=2","previous":null,
                     "all":[5,6],"results":[{"id":5,"title":"Facture","tags":[1]}]}
                    """);

            case "/api/documents/5/thumb/":
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                    {
                        Headers = { ContentType = new("image/webp") },
                    },
                };

            case "/api/documents/7/thumb/":
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            case "/api/tags/" when page == "1":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":2,"next":"http://paperless:8000/api/tags/?page=2","results":[
                      {"id":1,"name":"Banque","color":"#a6cee3","text_color":"#000000","is_inbox_tag":false,
                       "owner":3,"match":"secret","document_count":4}]}
                    """);

            case "/api/tags/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":2,"next":null,"results":[
                      {"id":2,"name":"Impôts","color":"#1f78b4","text_color":"#ffffff","is_inbox_tag":true}]}
                    """);

            case "/api/correspondents/":
                return FakePaperless.Json(HttpStatusCode.OK, """{"count":0,"next":null,"results":[]}""");

            default:
                return FakePaperless.Json(HttpStatusCode.NotFound, """{"detail":"Not found."}""");
        }
    }
}

public sealed class DocumentEndpointsTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
{
    private static int _nextIp;

    private readonly string _ip = $"198.51.100.{100 + Interlocked.Increment(ref _nextIp)}";

    private async Task<string> LoginAsync(string username = "alice", string? code = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new { username, password = "secret", code }),
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", _ip);
        request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        using var response = await fixture.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var header = response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(SessionTokens.CookieName + "=", StringComparison.Ordinal));
        return header.Split(';')[0][(SessionTokens.CookieName.Length + 1)..];
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", _ip);
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{SessionTokens.CookieName}={cookie}");
        }

        return await fixture.Http.SendAsync(request);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Dictionary<string, Microsoft.Extensions.Primitives.StringValues> LastPaperlessQuery() =>
        QueryHelpers.ParseQuery(fixture.Paperless.LastRequest!.RequestUri!.Query);

    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/documents/5/thumb")]
    [InlineData("/api/references/tags")]
    public async Task Sans_session_les_relais_repondent_401(string path)
    {
        using var response = await GetAsync(path, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Liste_transmet_le_token_et_les_seuls_parametres_autorises()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync(
            "/api/documents?page=2&ordering=-created&tags__id__all=1,2&title_content=facture&format=api&fields=content&page_size=1000",
            cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = fixture.Paperless.LastRequest!;
        Assert.Equal("/api/documents/", request.RequestUri!.AbsolutePath);
        Assert.Equal("tok-alice", request.Headers.Authorization!.Parameter);
        var query = LastPaperlessQuery();
        Assert.Equal("2", query["page"]);
        Assert.Equal("-created", query["ordering"]);
        Assert.Equal("1,2", query["tags__id__all"]);
        Assert.Equal("facture", query["title_content"]);
        Assert.Equal(DocumentEndpoints.MaxPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), query["page_size"]);
        Assert.Equal("true", query["truncate_content"]);
        Assert.False(query.ContainsKey("format"));
        Assert.False(query.ContainsKey("fields"));
    }

    [Fact]
    public async Task Liste_masque_les_liens_de_pagination_internes()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents", cookie);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("paperless:8000", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(30, json.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Facture", json.RootElement.GetProperty("results")[0].GetProperty("title").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("all").GetArrayLength());
    }

    [Theory]
    [InlineData(null, DocumentEndpoints.DefaultPageSize)]
    [InlineData("10", 10)]
    [InlineData("0", 1)]
    [InlineData("5000", DocumentEndpoints.MaxPageSize)]
    [InlineData("-3", DocumentEndpoints.DefaultPageSize)]
    [InlineData("abc", DocumentEndpoints.DefaultPageSize)]
    public void Taille_de_page_bornee(string? value, int expected) =>
        Assert.Equal(expected, DocumentEndpoints.PageSize(value));

    [Fact]
    public async Task Filtre_refuse_par_Paperless_donne_400()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents?ordering=inconnu", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("filtre_invalide", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Refus_de_Paperless_donne_403()
    {
        var cookie = await LoginAsync("bob", "123456");

        using var response = await GetAsync("/api/documents", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("acces_refuse", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Token_Paperless_refuse_revoque_la_session()
    {
        var cookie = await LoginAsync();
        var session = fixture.Store.Sessions.Values.Single(s => s.TokenHash.AsSpan().SequenceEqual(SessionTokens.Hash(cookie)));
        session.PaperlessToken = fixture.Services.GetRequiredService<PaperlessTokenProtector>().Protect("tok-supprime");

        using var response = await GetAsync("/api/documents", cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("non_connecte", await ErrorCodeAsync(response));
        Assert.NotNull(session.RevokedAt);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith(SessionTokens.CookieName + "=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Vignette_relayee_en_flux_avec_son_type()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/5/thumb", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromMinutes(10), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Vignette_inconnue_donne_404()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/6/thumb", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("introuvable", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Panne_de_Paperless_donne_503()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/7/thumb", cookie);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("paperless_indisponible", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Reference_parcourt_toutes_les_pages_et_ne_garde_que_les_champs_utiles()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/references/tags", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var tags = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(["Banque", "Impôts"], tags.Select(t => t.GetProperty("name").GetString()));
        Assert.Equal("#a6cee3", tags[0].GetProperty("color").GetString());
        Assert.True(tags[1].GetProperty("is_inbox_tag").GetBoolean());
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
        Assert.False(tags[0].TryGetProperty("owner", out _));
        Assert.Equal("2", LastPaperlessQuery()["page"]);
    }

    [Fact]
    public async Task Reference_vide()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/references/correspondants", cookie);

        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.Equal("/api/correspondents/", fixture.Paperless.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Reference_inconnue_donne_404_sans_appeler_Paperless()
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await GetAsync("/api/references/users", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("liste_inconnue", await ErrorCodeAsync(response));
        Assert.Equal(calls, fixture.Paperless.Calls);
    }
}
