using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Documents;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeGed.Api.Tests;

/// <summary>Routes du faux Paperless pour les écrans « Gérer » (types, chemins, champs, objets par identifiant).</summary>
internal static class FakePaperlessManage
{
    public static HttpResponseMessage? Respond(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method.Method;
        var managed = path.StartsWith("/api/document_types/", StringComparison.Ordinal)
            || path.StartsWith("/api/storage_paths/", StringComparison.Ordinal)
            || path.StartsWith("/api/custom_fields/", StringComparison.Ordinal)
            || path is "/api/tags/3/" or "/api/correspondents/4/";
        if (!managed)
        {
            return null;
        }

        var token = request.Headers.Authorization?.Parameter;
        if (token is not ("tok-alice" or "tok-bob"))
        {
            return FakePaperless.Json(HttpStatusCode.Unauthorized, """{"detail":"Invalid token."}""");
        }

        var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
        switch (path)
        {
            case "/api/document_types/" when method == "GET" && token == "tok-bob":
                return FakePaperless.Json(HttpStatusCode.Forbidden, """{"detail":"You do not have permission to perform this action."}""");

            case "/api/document_types/" when method == "GET":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":42,"next":"http://paperless:8000/api/document_types/?page=3","previous":"http://paperless:8000/api/document_types/?page=1",
                     "all":[1,2],"results":[{"id":1,"slug":"facture","name":"Facture","match":"edf","matching_algorithm":1,"is_insensitive":true,
                     "document_count":12,"owner":1,"user_can_change":true,
                     "permissions":{"view":{"users":[2],"groups":[]},"change":{"users":[],"groups":[1]}}}]}
                    """);

            case "/api/storage_paths/" when method == "POST":
                var sent = JsonNode.Parse(body!)!.AsObject();
                sent["id"] = 7;
                sent["slug"] = "banque";
                sent["document_count"] = 0;
                return FakePaperless.Json(HttpStatusCode.Created, sent.ToJsonString());

            case "/api/custom_fields/" when method == "POST":
                return FakePaperless.Json(HttpStatusCode.Created, """
                    {"id":8,"name":"Personne","data_type":"select","document_count":0,
                     "extra_data":{"select_options":[{"id":"aB1","label":"Kévin"}],"default_currency":null}}
                    """);

            case "/api/custom_fields/" when method == "GET":
                return FakePaperless.Json(HttpStatusCode.OK, """{"count":0,"next":null,"results":[]}""");

            case "/api/tags/3/" when method == "PATCH" && token == "tok-bob":
                return FakePaperless.Json(HttpStatusCode.Forbidden, """{"detail":"You do not have permission to perform this action."}""");

            case "/api/tags/3/" when method == "PATCH" && body!.Contains("Doublon", StringComparison.Ordinal):
                return FakePaperless.Json(HttpStatusCode.BadRequest, """{"name":["Tag with this name already exists."]}""");

            case "/api/tags/3/" when method == "PATCH" && query.ContainsKey("full_perms"):
                var patched = JsonNode.Parse(body!)!.AsObject();
                patched["id"] = 3;
                patched["slug"] = "x";
                patched["permissions"] = patched["set_permissions"]?.DeepClone();
                patched.Remove("set_permissions");
                return FakePaperless.Json(HttpStatusCode.OK, patched.ToJsonString());

            case "/api/correspondents/4/" when method == "DELETE":
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            case "/api/storage_paths/99/" when method == "DELETE":
                return FakePaperless.Json(HttpStatusCode.NotFound, """{"detail":"No StoragePath matches the given query."}""");

            default:
                return FakePaperless.Json(HttpStatusCode.NotFound, """{"detail":"Not found."}""");
        }
    }
}

public sealed class ManageEndpointsTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
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

    private Dictionary<string, Microsoft.Extensions.Primitives.StringValues> LastPaperlessQuery() =>
        QueryHelpers.ParseQuery(fixture.Paperless.LastRequest!.RequestUri!.Query);

    private JsonElement LastPaperlessBody() => JsonElement.Parse(fixture.Paperless.LastBody!);

    [Fact]
    public async Task Sans_session_401()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/gestion/tags", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Modification_sans_en_tete_anti_falsification_refusee()
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Delete, "/api/gestion/correspondants/4", cookie, antiForgery: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Liste_paginee_avec_recherche_tri_et_permissions_completes()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(
            HttpMethod.Get, "/api/gestion/types?page=2&page_size=500&nom=%20fact%20&tri=-document_count&format=api", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/document_types/", fixture.Paperless.LastRequest!.RequestUri!.AbsolutePath);
        var query = LastPaperlessQuery();
        Assert.Equal("2", query["page"]);
        Assert.Equal(ManageEndpoints.MaxPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), query["page_size"]);
        Assert.Equal("fact", query["name__icontains"]);
        Assert.Equal("-document_count", query["ordering"]);
        Assert.Equal("true", query["full_perms"]);
        Assert.False(query.ContainsKey("format"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("paperless:8000", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(42, json.RootElement.GetProperty("count").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("all", out _));
        var item = json.RootElement.GetProperty("results")[0];
        Assert.Equal("Facture", item.GetProperty("name").GetString());
        Assert.Equal(12, item.GetProperty("document_count").GetInt32());
        Assert.Equal(1, item.GetProperty("matching_algorithm").GetInt32());
        Assert.True(item.GetProperty("user_can_change").GetBoolean());
        Assert.Equal(1, item.GetProperty("permissions").GetProperty("change").GetProperty("groups")[0].GetInt32());
        Assert.False(item.TryGetProperty("slug", out _));
    }

    [Theory]
    [InlineData("inconnu", "name")]
    [InlineData("-slug", "name")]
    [InlineData("matching_algorithm", "matching_algorithm")]
    [InlineData(null, "name")]
    public void Tri_en_liste_blanche(string? requested, string expected) =>
        Assert.Equal(expected, ManageEndpoints.Ordering(requested));

    [Fact]
    public async Task Champs_personnalises_sans_permissions_completes()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Get, "/api/gestion/champs", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(LastPaperlessQuery().ContainsKey("full_perms"));
        Assert.Equal("""{"count":0,"results":[]}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Liste_refusee_par_Paperless()
    {
        var cookie = await LoginAsync("bob", "123456");

        using var response = await SendAsync(HttpMethod.Get, "/api/gestion/types", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("acces_refuse", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("GET", "/api/gestion/utilisateurs")]
    [InlineData("POST", "/api/gestion/groupes")]
    [InlineData("PATCH", "/api/gestion/documents/5")]
    [InlineData("DELETE", "/api/gestion/users/1")]
    public async Task Liste_inconnue_404_sans_appeler_Paperless(string method, string path)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(new HttpMethod(method), path, cookie, method is "POST" or "PATCH" ? """{"name":"x"}""" : null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("liste_inconnue", await ErrorCodeAsync(response));
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Creation_de_chemin_avec_correspondance_choisie()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/gestion/chemins", cookie, """
            {"name":" Banque ","path":"{{ correspondent }}/{{ title }}","match":"BNP ","matching_algorithm":3,"is_insensitive":false}
            """);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = LastPaperlessBody();
        Assert.Equal("Banque", sent.GetProperty("name").GetString());
        Assert.Equal("{{ correspondent }}/{{ title }}", sent.GetProperty("path").GetString());
        Assert.Equal("BNP ", sent.GetProperty("match").GetString());
        Assert.Equal(3, sent.GetProperty("matching_algorithm").GetInt32());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("slug", out _));
    }

    [Fact]
    public async Task Creation_en_classement_auto_par_defaut()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/gestion/tags", cookie, """{"name":"Nouveau","color":"#FF0000","is_inbox_tag":true}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = LastPaperlessBody();
        Assert.Equal(DocumentEndpoints.MatchAuto, sent.GetProperty("matching_algorithm").GetInt32());
        Assert.Equal("#ff0000", sent.GetProperty("color").GetString());
        Assert.True(sent.GetProperty("is_inbox_tag").GetBoolean());
    }

    [Fact]
    public async Task Creation_de_champ_liste_de_choix()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/gestion/champs", cookie, """
            {"name":"Personne","data_type":"select","extra_data":{"select_options":[{"label":" Kévin "},{"id":"aB1","label":"Lou"}]}}
            """);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = LastPaperlessBody();
        Assert.Equal("/api/custom_fields/", fixture.Paperless.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("select", sent.GetProperty("data_type").GetString());
        Assert.False(sent.TryGetProperty("matching_algorithm", out _));
        Assert.False(sent.TryGetProperty("set_permissions", out _));
        var options = sent.GetProperty("extra_data").GetProperty("select_options");
        Assert.Equal("Kévin", options[0].GetProperty("label").GetString());
        Assert.False(options[0].TryGetProperty("id", out _));
        Assert.Equal("aB1", options[1].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("tags", """{"name":""}""")]
    [InlineData("tags", """{"color":"#ff0000"}""")]
    [InlineData("tags", """{"name":"x","color":"rouge"}""")]
    [InlineData("tags", """{"name":"x","matching_algorithm":7}""")]
    [InlineData("tags", """{"name":"x","matching_algorithm":"1"}""")]
    [InlineData("tags", """{"name":"x","is_insensitive":"oui"}""")]
    [InlineData("tags", """{"name":"x","path":"a"}""")]
    [InlineData("tags", """{"name":"x","slug":"y"}""")]
    [InlineData("tags", """{"name":"x","owner":1}""")]
    [InlineData("correspondants", """{"name":"x","color":"#ffffff"}""")]
    [InlineData("chemins", """{"name":"Banque"}""")]
    [InlineData("champs", """{"name":"x"}""")]
    [InlineData("champs", """{"name":"x","data_type":"blob"}""")]
    [InlineData("champs", """{"name":"x","data_type":"string","match":"a"}""")]
    [InlineData("champs", """{"name":"x","data_type":"select","extra_data":{"select_options":[{"label":""}]}}""")]
    [InlineData("champs", """{"name":"x","data_type":"select","extra_data":{"select_options":[{"id":"a","label":"A"},{"id":"a","label":"B"}]}}""")]
    [InlineData("champs", """{"name":"x","data_type":"monetary","extra_data":{"default_currency":"EURO"}}""")]
    [InlineData("champs", """{"name":"x","data_type":"string","extra_data":{"autre":1}}""")]
    [InlineData("champs", """{"name":"x","data_type":"string","owner":1}""")]
    public async Task Creation_invalide_refusee_sans_appeler_Paperless(string kind, string body)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Post, $"/api/gestion/{kind}", cookie, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("requete_invalide", await ErrorCodeAsync(response));
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Modification_avec_permissions()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Patch, "/api/gestion/tags/3", cookie, """
            {"name":"Renommé","owner":null,"set_permissions":{"view":{"users":[2,2],"groups":[]},"change":{"users":[],"groups":[1]}}}
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = LastPaperlessBody();
        Assert.Equal("Renommé", sent.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("owner").ValueKind);
        Assert.Equal(1, sent.GetProperty("set_permissions").GetProperty("view").GetProperty("users").GetArrayLength());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("permissions").GetProperty("change").GetProperty("groups")[0].GetInt32());
        Assert.False(json.RootElement.TryGetProperty("slug", out _));
    }

    [Theory]
    [InlineData("""{"data_type":"string"}""")]
    [InlineData("""{}""")]
    public async Task Type_de_champ_non_modifiable(string body)
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Patch, "/api/gestion/champs/8", cookie, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nom_en_double_message_de_Paperless()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Patch, "/api/gestion/tags/3", cookie, """{"name":"Doublon"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("modification_refusee", await ErrorCodeAsync(response));
        Assert.Contains("already exists", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Modification_refusee_par_Paperless()
    {
        var cookie = await LoginAsync("bob", "123456");

        using var response = await SendAsync(HttpMethod.Patch, "/api/gestion/tags/3", cookie, """{"name":"x"}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("acces_refuse", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Suppression()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Delete, "/api/gestion/correspondants/4", cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Delete, fixture.Paperless.LastRequest!.Method);
        Assert.Equal("/api/correspondents/4/", fixture.Paperless.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Suppression_d_un_element_introuvable()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Delete, "/api/gestion/chemins/99", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("introuvable", await ErrorCodeAsync(response));
    }
}
