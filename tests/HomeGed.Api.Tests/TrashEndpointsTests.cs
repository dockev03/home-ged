using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Documents;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeGed.Api.Tests;

/// <summary>Routes du faux Paperless pour la corbeille et les suggestions.</summary>
internal static class FakePaperlessTrash
{
    public static HttpResponseMessage? Respond(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path != "/api/trash/" && !(path.StartsWith("/api/documents/", StringComparison.Ordinal) && path.EndsWith("/suggestions/", StringComparison.Ordinal)))
        {
            return null;
        }

        if (request.Headers.Authorization?.Parameter is not ("tok-alice" or "tok-bob"))
        {
            return FakePaperless.Json(HttpStatusCode.Unauthorized, """{"detail":"Invalid token."}""");
        }

        var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
        var forbidden = body is not null && JsonNode.Parse(body)?["documents"] is JsonArray ids && ids.Any(i => i!.GetValue<int>() == 99);
        switch (path)
        {
            case "/api/trash/" when request.Method == HttpMethod.Get && query["page"] == "9":
                return FakePaperless.Json(HttpStatusCode.NotFound, """{"detail":"Invalid page."}""");

            case "/api/trash/" when request.Method == HttpMethod.Get:
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":2,"next":"http://paperless:8000/api/trash/?page=2","previous":null,"all":[12,14],"results":[
                     {"id":12,"title":"Facture EDF","correspondent":3,"document_type":null,"tags":[1],"created":"2026-10-01",
                      "deleted_at":"2026-11-20T10:00:00Z","content":"secret","original_file_name":"edf.pdf","owner":1},
                     {"id":14,"title":"Devis","correspondent":null,"document_type":2,"tags":[],"created":"2026-09-12",
                      "deleted_at":"2026-11-02T08:30:00Z","content":"secret","owner":1}]}
                    """);

            case "/api/trash/" when forbidden:
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Insufficient permissions") };

            case "/api/trash/":
                return FakePaperless.Json(HttpStatusCode.OK, """{"result":"OK","doc_ids":[12]}""");

            case "/api/documents/7/suggestions/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"correspondents":[3,"x",-1],"tags":[1,2,2],"document_types":[],"storage_paths":[4],
                     "dates":["2026-03-01","pas une date","2026-03-01T00:00:00Z","2025-12-24"],"suggested_tags":["secret"]}
                    """);

            default:
                return FakePaperless.Json(HttpStatusCode.NotFound, """{"detail":"No Document matches the given query."}""");
        }
    }
}

public sealed class TrashEndpointsTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
{
    private static int _nextIp;

    private readonly string _ip = $"198.51.100.{200 + Interlocked.Increment(ref _nextIp)}";

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

    private List<(HttpMethod Method, string PathAndQuery, string? Body)> Since(int from) =>
        [.. fixture.Paperless.History.Skip(from)];

    [Fact]
    public async Task Sans_session_401()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/corbeille", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Liste_avec_delai_de_conservation_sans_contenu_ni_adresse_interne()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Get, "/api/corbeille?page=abc", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var calls = Since(from);
        Assert.Equal("/api/ui_settings/", calls[0].PathAndQuery);
        Assert.Equal("/api/trash/?page=1&page_size=25", calls[1].PathAndQuery);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("paperless:8000", body, StringComparison.Ordinal);
        Assert.DoesNotContain("edf.pdf", body, StringComparison.Ordinal);
        var json = JsonNode.Parse(body)!;
        Assert.Equal(2, json["count"]!.GetValue<int>());
        Assert.Equal(30, json["delai"]!.GetValue<int>());
        var first = json["results"]![0]!.AsObject();
        Assert.Equal(["id", "title", "correspondent", "document_type", "tags", "created", "deleted_at"], first.Select(p => p.Key));
        Assert.Equal("2026-11-20T10:00:00Z", first["deleted_at"]!.GetValue<string>());
    }

    [Fact]
    public async Task Liste_sans_delai_connu()
    {
        var cookie = await LoginAsync("bob", "123456");

        using var response = await SendAsync(HttpMethod.Get, "/api/corbeille?page=2", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Null(json["delai"]);
        Assert.Equal(2, json["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task Page_au_dela_de_la_derniere_liste_vide()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Get, "/api/corbeille?page=9", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"count":0,"delai":30,"results":[]}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Restauration_des_documents_choisis()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Post, "/api/corbeille/restauration", cookie, """{"documents":[14,12,14]}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var call = Assert.Single(Since(from));
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/api/trash/", call.PathAndQuery);
        Assert.Equal("""{"action":"restore","documents":[12,14]}""", call.Body);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"documents":[]}""")]
    [InlineData("""{"documents":[0]}""")]
    [InlineData("""{"documents":["12"]}""")]
    [InlineData("""{"tout":true}""")]
    public async Task Restauration_invalide_refusee_sans_appel(string json)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Post, "/api/corbeille/restauration", cookie, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("requete_invalide", await ErrorCodeAsync(response));
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Suppression_definitive_des_documents_choisis()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Post, "/api/corbeille/suppression", cookie, """{"documents":[12]}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("""{"action":"empty","documents":[12]}""", Assert.Single(Since(from)).Body);
    }

    [Fact]
    public async Task Vider_toute_la_corbeille_seulement_sur_demande_explicite()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Post, "/api/corbeille/suppression", cookie, """{"tout":true}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("""{"action":"empty"}""", Assert.Single(Since(from)).Body);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"tout":false}""")]
    [InlineData("""{"tout":"true"}""")]
    [InlineData("""{"documents":null}""")]
    [InlineData("""{"tout":true,"documents":[12]}""")]
    public async Task Suppression_sans_selection_explicite_n_efface_rien(string json)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Post, "/api/corbeille/suppression", cookie, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Suppression_refusee_par_paperless()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/corbeille/suppression", cookie, """{"documents":[12,99]}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("acces_refuse", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Suppression_sans_en_tete_anti_falsification_refusee()
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Post, "/api/corbeille/suppression", cookie, """{"tout":true}""", antiForgery: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public void Trop_de_documents_refuses()
    {
        var ids = new JsonArray([.. Enumerable.Range(1, TrashEndpoints.MaxDocuments + 1).Select(i => (JsonNode)i)]);

        var (result, error) = TrashEndpoints.ReadIds(ids);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Suggestions_filtrees_et_validees()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Get, "/api/documents/7/suggestions", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/documents/7/suggestions/", Assert.Single(Since(from)).PathAndQuery);
        Assert.Equal(
            """{"tags":[1,2],"correspondents":[3],"document_types":[],"storage_paths":[4],"dates":["2026-03-01","2025-12-24"]}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Suggestions_document_introuvable()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Get, "/api/documents/8/suggestions", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("introuvable", await ErrorCodeAsync(response));
    }
}
