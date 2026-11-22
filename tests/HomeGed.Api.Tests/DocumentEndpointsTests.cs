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
    public static HttpResponseMessage? Respond(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith("/api/documents/", StringComparison.Ordinal)
            && !path.StartsWith("/api/tags/", StringComparison.Ordinal)
            && !path.StartsWith("/api/correspondents/", StringComparison.Ordinal)
            && path is not ("/api/tasks/" or "/api/users/" or "/api/groups/"))
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
        var method = request.Method.Method;
        var fullPerms = query.ContainsKey("full_perms");
        switch (path)
        {
            case "/api/documents/5/" when fullPerms && method == "PATCH" && token == "tok-bob":
                return FakePaperless.Json(HttpStatusCode.Forbidden, """{"detail":"Insufficient permissions."}""");

            case "/api/documents/5/" when fullPerms && method == "PATCH":
                using (var sent = JsonDocument.Parse(body!))
                {
                    return FakePaperless.Json(HttpStatusCode.OK, $$"""
                        {"id":5,"title":"Facture","owner":{{sent.RootElement.GetProperty("owner").GetRawText()}},
                         "permissions":{{sent.RootElement.GetProperty("set_permissions").GetRawText()}}}
                        """);
                }

            case "/api/documents/5/" when fullPerms:
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"id":5,"title":"Facture","content":"secret","owner":1,
                     "permissions":{"view":{"users":[2],"groups":[1]},"change":{"users":[],"groups":[]}}}
                    """);

            case "/api/documents/5/history/" when token == "tok-bob":
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Insufficient permissions") };

            case "/api/documents/5/history/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    [{"id":1,"timestamp":"2026-11-02T10:00:00Z","action":"update","changes":{"title":["A","B"]},"actor":{"id":1,"username":"alice"}}]
                    """);

            case "/api/documents/6/history/":
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Audit log is disabled") };

            case "/api/documents/5/update_version/":
                return request.Content!.Headers.ContentType!.MediaType == "multipart/form-data"
                    ? FakePaperless.Json(HttpStatusCode.OK, "\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"")
                    : FakePaperless.Json(HttpStatusCode.BadRequest, """{"document":["No file was submitted."]}""");

            case "/api/documents/5/versions/7/" when method == "PATCH":
                return FakePaperless.Json(HttpStatusCode.OK, body!);

            case "/api/documents/5/versions/7/" when method == "DELETE":
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            case "/api/documents/5/versions/9/" when method == "DELETE":
                return FakePaperless.Json(HttpStatusCode.BadRequest, """{"detail":"Cannot delete the root/original version."}""");

            case "/api/tasks/" when query["task_id"] == "3f2504e0-4f89-11d3-9a0c-0305e82c3301":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":1,"next":null,"results":[{"id":4,"task_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","status":"success",
                      "input_data":{"filename":"facture.pdf"},"result_data":{"document_id":12},"related_document_ids":[12],"owner":1}]}
                    """);

            case "/api/tasks/" when query["task_id"] == "3f2504e0-4f89-11d3-9a0c-0305e82c3302":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":1,"next":null,"results":[{"id":5,"task_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3302","status":"failure",
                      "result_data":{"reason":"Not consuming facture.pdf: It is a duplicate of Facture (#5)."},"related_document_ids":[]}]}
                    """);

            case "/api/tasks/":
                return FakePaperless.Json(HttpStatusCode.OK, """{"count":0,"next":null,"results":[]}""");

            case "/api/users/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":1,"next":null,"results":[{"id":2,"username":"bob","first_name":"Bob","last_name":"M.","email":"bob@example.test",
                      "password":"**********","is_superuser":false,"user_permissions":["view_document"],"groups":[1]}]}
                    """);

            case "/api/groups/":
                return FakePaperless.Json(HttpStatusCode.OK, """{"count":1,"next":null,"results":[{"id":1,"name":"Foyer","permissions":["view_document"]}]}""");

            case "/api/documents/5/" when method == "GET":
                return FakePaperless.Json(HttpStatusCode.OK, """{"id":5,"title":"Facture","content":"Texte complet","tags":[1]}""");

            case "/api/documents/5/" when method == "PATCH" && body!.Contains("refus", StringComparison.Ordinal):
                return FakePaperless.Json(HttpStatusCode.BadRequest, """{"title":["Titre refusé."],"custom_fields":[{"value":["Valeur invalide."]}]}""");

            case "/api/documents/5/" when method == "PATCH":
                return FakePaperless.Json(HttpStatusCode.OK, body!);

            case "/api/documents/5/" when method == "DELETE":
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            case "/api/documents/5/preview/" or "/api/documents/5/download/":
                var inline = path.Contains("preview", StringComparison.Ordinal);
                var file = new ByteArrayContent([37, 80, 68, 70]);
                file.Headers.ContentType = new("application/pdf");
                file.Headers.ContentDisposition = new(inline ? "inline" : "attachment") { FileName = query.ContainsKey("original") ? "original.pdf" : "archive.pdf" };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = file };

            case "/api/documents/5/metadata/":
                return FakePaperless.Json(HttpStatusCode.OK, """{"original_checksum":"abc","has_archive_version":true}""");

            case "/api/documents/5/notes/" when method == "GET":
                return FakePaperless.Json(HttpStatusCode.OK, """[{"id":3,"note":"À payer","created":"2026-11-01T10:00:00Z","user":{"id":1,"username":"alice"}}]""");

            case "/api/documents/5/notes/" when method == "POST":
                return FakePaperless.Json(HttpStatusCode.OK, """[{"id":4,"note":"nouvelle"}]""");

            case "/api/documents/5/notes/" when method == "DELETE":
                return FakePaperless.Json(HttpStatusCode.OK, "[]");

            case "/api/tags/" when method == "POST":
                return FakePaperless.Json(HttpStatusCode.Created, """{"id":9,"name":"Nouveau","color":"#ff0000","text_color":"#ffffff","is_inbox_tag":false,"owner":1,"matching_algorithm":6}""");

            case "/api/documents/" when ordering == "inconnu":
                return FakePaperless.Json(HttpStatusCode.BadRequest, """{"ordering":["invalide"]}""");

            case "/api/documents/" when token == "tok-bob":
                return FakePaperless.Json(HttpStatusCode.Forbidden, """{"detail":"Forbidden"}""");

            case "/api/documents/" when query.ContainsKey("fields"):
                return FakePaperless.Json(HttpStatusCode.OK, """{"count":3,"next":null,"results":[{"id":7},{"id":3},{"id":9}]}""");

            case "/api/documents/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":30,"next":"http://paperless:8000/api/documents/?page=2","previous":null,
                     "results":[{"id":5,"title":"Facture","tags":[1]}]}
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

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, object? body = null, bool antiForgery = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", _ip);
        request.Headers.TryAddWithoutValidation("Cookie", $"{SessionTokens.CookieName}={cookie}");
        if (antiForgery)
        {
            request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        }

        if (body is not null)
        {
            request.Content = body switch
            {
                HttpContent content => content,
                string json => new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                _ => JsonContent.Create(body),
            };
        }

        return await fixture.Http.SendAsync(request);
    }

    private JsonElement LastPaperlessBody() => JsonElement.Parse(fixture.Paperless.LastBody!);

    [Fact]
    public async Task Fiche_relayee_avec_le_contenu_complet()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/5", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/documents/5/", fixture.Paperless.LastRequest!.RequestUri!.AbsolutePath);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Texte complet", json.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Modification_ne_transmet_que_les_champs_autorises_et_normalises()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Patch, "/api/documents/5", cookie, """
            {"title":"  Facture EDF  ","tags":[3,1,3],"correspondent":null,"created":"2026-11-02",
             "archive_serial_number":42,"custom_fields":[{"field":2,"value":"EUR12.50"},{"field":4,"value":null}]}
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Patch, fixture.Paperless.LastRequest!.Method);
        var sent = LastPaperlessBody();
        Assert.Equal("Facture EDF", sent.GetProperty("title").GetString());
        Assert.Equal([1, 3], sent.GetProperty("tags").EnumerateArray().Select(t => t.GetInt32()));
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("correspondent").ValueKind);
        Assert.Equal("EUR12.50", sent.GetProperty("custom_fields")[0].GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("custom_fields")[1].GetProperty("value").ValueKind);
    }

    [Theory]
    [InlineData("""{"owner":2}""")]
    [InlineData("""{"set_permissions":{}}""")]
    [InlineData("""{"title":""}""")]
    [InlineData("""{"title":3}""")]
    [InlineData("""{"created":"02/11/2026"}""")]
    [InlineData("""{"tags":[0]}""")]
    [InlineData("""{"tags":"1"}""")]
    [InlineData("""{"correspondent":"2"}""")]
    [InlineData("""{"archive_serial_number":-1}""")]
    [InlineData("""{"custom_fields":[{"field":2,"value":1,"document":5}]}""")]
    [InlineData("""{"custom_fields":[{"field":2},{"field":2}]}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    public async Task Modification_invalide_refusee_sans_appeler_Paperless(string body)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Patch, "/api/documents/5", cookie, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("requete_invalide", await ErrorCodeAsync(response));
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Modification_refusee_par_Paperless_renvoie_ses_messages()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Patch, "/api/documents/5", cookie, new { title = "refus" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("modification_refusee", await ErrorCodeAsync(response));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var title = json.RootElement.GetProperty("title").GetString();
        Assert.Contains("title : Titre refusé.", title, StringComparison.Ordinal);
        Assert.Contains("custom_fields : Valeur invalide.", title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ecriture_sans_en_tete_anti_CSRF_refusee()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Delete, "/api/documents/5", cookie, antiForgery: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpMethod.Delete, fixture.Paperless.LastRequest!.Method);
    }

    [Fact]
    public async Task Suppression_relayee()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Delete, "/api/documents/5", cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Delete, fixture.Paperless.LastRequest!.Method);
        Assert.Equal("/api/documents/5/", fixture.Paperless.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Apercu_seule_reponse_affichable_dans_un_cadre_du_site()
    {
        var cookie = await LoginAsync();

        using var preview = await GetAsync("/api/documents/5/preview", cookie);
        using var download = await GetAsync("/api/documents/5/download?original=true", cookie);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("application/pdf", preview.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", preview.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("SAMEORIGIN", preview.Headers.GetValues("X-Frame-Options").Single());
        var csp = preview.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal(4, preview.Content.Headers.ContentLength);

        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("original.pdf", download.Content.Headers.ContentDisposition.FileName);
        Assert.Equal("DENY", download.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", download.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("image/webp", true)]
    [InlineData("text/plain", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("text/html", false)]
    [InlineData("application/octet-stream", false)]
    public void Seuls_les_types_inertes_s_affichent_dans_le_cadre(string type, bool expected) =>
        Assert.Equal(expected, DocumentEndpoints.IsInertPreviewType(type));

    [Fact]
    public async Task Metadonnees_relayees()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/5/metadata", cookie);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("abc", json.RootElement.GetProperty("original_checksum").GetString());
    }

    [Fact]
    public async Task Notes_lister_ajouter_supprimer()
    {
        var cookie = await LoginAsync();

        using var list = await GetAsync("/api/documents/5/notes", cookie);
        Assert.Contains("À payer", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var added = await SendAsync(HttpMethod.Post, "/api/documents/5/notes", cookie, new { note = "  nouvelle  " });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal("nouvelle", LastPaperlessBody().GetProperty("note").GetString());

        using var deleted = await SendAsync(HttpMethod.Delete, "/api/documents/5/notes/3", cookie);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpMethod.Delete, fixture.Paperless.LastRequest!.Method);
        Assert.Equal("?id=3", fixture.Paperless.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task Note_vide_refusee()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/documents/5/notes", cookie, new { note = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creation_a_la_volee_en_classement_auto()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/references/tags", cookie, new { name = " Nouveau ", color = "#ff0000" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = LastPaperlessBody();
        Assert.Equal("Nouveau", sent.GetProperty("name").GetString());
        Assert.Equal("#ff0000", sent.GetProperty("color").GetString());
        Assert.Equal(DocumentEndpoints.MatchAuto, sent.GetProperty("matching_algorithm").GetInt32());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(9, json.RootElement.GetProperty("id").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("owner", out _));
    }

    [Theory]
    [InlineData("tags", """{"name":""}""")]
    [InlineData("tags", """{"name":"x","color":"rouge"}""")]
    [InlineData("chemins", """{"name":"Banque"}""")]
    public async Task Creation_invalide_refusee(string kind, string body)
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, $"/api/references/{kind}", cookie, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creation_de_champ_personnalise_non_proposee()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, "/api/references/champs", cookie, new { name = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Permissions_par_defaut_reprises_des_reglages_Paperless()
    {
        var target = new System.Text.Json.Nodes.JsonObject();
        var settings = System.Text.Json.Nodes.JsonNode.Parse("""
            {"settings":{"permissions":{"default_owner":null,"default_view_users":[2,"x"],"default_view_groups":[1],
             "default_edit_users":[],"default_edit_groups":[1]}}}
            """)!.AsObject();

        DocumentEndpoints.ApplyDefaultPermissions(settings, target);

        Assert.True(target.ContainsKey("owner"));
        Assert.Null(target["owner"]);
        Assert.Equal("""{"view":{"users":[2],"groups":[1]},"change":{"users":[],"groups":[1]}}""", target["set_permissions"]!.ToJsonString());
    }

    [Fact]
    public void Sans_reglage_Paperless_garde_son_comportement()
    {
        var target = new System.Text.Json.Nodes.JsonObject();

        DocumentEndpoints.ApplyDefaultPermissions(System.Text.Json.Nodes.JsonNode.Parse("""{"settings":{}}""")!.AsObject(), target);

        Assert.Empty(target);
    }

    [Fact]
    public async Task Historique_relaye()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/5/history", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"title\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Historique_reserve_et_desactive_expliques()
    {
        var bob = await LoginAsync("bob", "123456");
        using var refused = await GetAsync("/api/documents/5/history", bob);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("historique_reserve", await ErrorCodeAsync(refused));

        var alice = await LoginAsync();
        using var disabled = await GetAsync("/api/documents/6/history", alice);
        Assert.Equal(HttpStatusCode.BadRequest, disabled.StatusCode);
        Assert.Equal("historique_desactive", await ErrorCodeAsync(disabled));
    }

    [Fact]
    public async Task Permissions_lues_sans_le_reste_du_document()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/5/permissions", cookie);

        Assert.Equal("full_perms=true", fixture.Paperless.LastRequest!.RequestUri!.Query.TrimStart('?'));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("""{"owner":1,"permissions":{"view":{"users":[2],"groups":[1]},"change":{"users":[],"groups":[]}}}""", body);
    }

    [Fact]
    public async Task Permissions_modifiees_par_liste_blanche()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Patch, "/api/documents/5/permissions", cookie, """
            {"owner":null,"set_permissions":{"view":{"users":[3,2,3],"groups":[1]},"change":{"users":[],"groups":[]}}}
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Patch, fixture.Paperless.LastRequest!.Method);
        Assert.Equal(
            """{"owner":null,"set_permissions":{"view":{"users":[2,3],"groups":[1]},"change":{"users":[],"groups":[]}}}""",
            fixture.Paperless.LastBody);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("owner").ValueKind);
    }

    [Theory]
    [InlineData("""{"set_permissions":{"view":{"users":[1]}}}""")]
    [InlineData("""{"owner":1}""")]
    [InlineData("""{"owner":"1","set_permissions":{"view":{},"change":{}}}""")]
    [InlineData("""{"title":"x","set_permissions":{"view":{},"change":{}}}""")]
    [InlineData("""{"set_permissions":{"view":{"users":[0]},"change":{}}}""")]
    [InlineData("""{"set_permissions":{"view":{},"change":{},"delete":{}}}""")]
    public async Task Permissions_invalides_refusees(string body)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Patch, "/api/documents/5/permissions", cookie, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Permissions_refusees_a_qui_n_est_pas_proprietaire()
    {
        var cookie = await LoginAsync("bob", "123456");

        using var response = await SendAsync(HttpMethod.Patch, "/api/documents/5/permissions", cookie,
            """{"set_permissions":{"view":{},"change":{}}}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("propriétaire", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Utilisateurs_sans_email_ni_droits()
    {
        var cookie = await LoginAsync();

        using var users = await GetAsync("/api/references/utilisateurs", cookie);
        using var groups = await GetAsync("/api/references/groupes", cookie);

        Assert.Equal("""[{"id":2,"username":"bob","first_name":"Bob","last_name":"M."}]""", await users.Content.ReadAsStringAsync());
        Assert.Equal("""[{"id":1,"name":"Foyer"}]""", await groups.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("utilisateurs")]
    [InlineData("groupes")]
    [InlineData("champs")]
    public async Task Pas_de_creation_hors_des_listes_prevues(string kind)
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Post, $"/api/references/{kind}", cookie, new { name = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Nouvelle_version_relayee_en_flux()
    {
        var cookie = await LoginAsync();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent([37, 80, 68, 70]) { Headers = { ContentType = new("application/pdf") } }, "document", "v2.pdf" },
            { new StringContent("Signée"), "version_label" },
        };

        using var response = await SendAsync(HttpMethod.Post, "/api/documents/5/versions", cookie, form);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("""{"tache":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""", await response.Content.ReadAsStringAsync());
        var sent = fixture.Paperless.LastRequest!;
        Assert.Equal("/api/documents/5/update_version/", sent.RequestUri!.AbsolutePath);
        Assert.Equal("tok-alice", sent.Headers.Authorization!.Parameter);
        Assert.Contains("name=document; filename=v2.pdf", fixture.Paperless.LastBody, StringComparison.Ordinal);
        Assert.Contains("Signée", fixture.Paperless.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nouvelle_version_exige_un_envoi_de_fichier()
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Post, "/api/documents/5/versions", cookie, new { document = "x" });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Version_renommee_et_supprimee()
    {
        var cookie = await LoginAsync();

        using var renamed = await SendAsync(HttpMethod.Patch, "/api/documents/5/versions/7", cookie, new { versionLabel = "  Signée  " });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Signée", LastPaperlessBody().GetProperty("version_label").GetString());

        using var cleared = await SendAsync(HttpMethod.Patch, "/api/documents/5/versions/7", cookie, new { versionLabel = "" });
        Assert.Equal("""{"version_label":null}""", fixture.Paperless.LastBody);

        using var deleted = await SendAsync(HttpMethod.Delete, "/api/documents/5/versions/7", cookie);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var root = await SendAsync(HttpMethod.Delete, "/api/documents/5/versions/9", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, root.StatusCode);
        Assert.Contains("root/original", await root.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Libelle_de_version_trop_long_refuse()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Patch, "/api/documents/5/versions/7", cookie, new { versionLabel = new string('x', 65) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Apercu_d_une_version_precise()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/5/preview?version=7&original=true&format=json", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?original=true&version=7", fixture.Paperless.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Tache_reduite_a_son_etat()
    {
        var cookie = await LoginAsync();

        using var done = await GetAsync("/api/taches/3f2504e0-4f89-11d3-9a0c-0305e82c3301", cookie);
        using var failed = await GetAsync("/api/taches/3f2504e0-4f89-11d3-9a0c-0305e82c3302", cookie);
        using var unknown = await GetAsync("/api/taches/00000000-0000-0000-0000-000000000001", cookie);

        Assert.Equal("""{"status":"success","message":null,"document_id":12}""", await done.Content.ReadAsStringAsync());
        var failure = JsonElement.Parse(await failed.Content.ReadAsStringAsync());
        Assert.Equal("failure", failure.GetProperty("status").GetString());
        Assert.Contains("duplicate", failure.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("""{"status":"pending","message":null,"document_id":null}""", await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Identifiants_de_tous_les_resultats_dans_l_ordre()
    {
        var cookie = await LoginAsync();

        using var response = await GetAsync("/api/documents/ids?tags__id__all=1&ordering=-created&page=3&page_size=5&fields=content", cookie);

        Assert.Equal("[7,3,9]", await response.Content.ReadAsStringAsync());
        var query = LastPaperlessQuery();
        Assert.Equal("1", query["tags__id__all"]);
        Assert.Equal("-created", query["ordering"]);
        Assert.Equal("id", query["fields"]);
        Assert.Equal("100000", query["page_size"]);
        Assert.False(query.ContainsKey("page"));
    }
}
