using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Documents;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeGed.Api.Tests;

/// <summary>Routes du faux Paperless pour les vues sauvegardées et le tableau de bord.</summary>
internal static class FakePaperlessViews
{
    public static HttpResponseMessage? Respond(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
        var handled = path.StartsWith("/api/saved_views/", StringComparison.Ordinal)
            || path == "/api/statistics/"
            || (path == "/api/custom_fields/" && query.ContainsKey("name__iexact"))
            || (path == "/api/documents/" && query.ContainsKey("custom_field_query"));
        if (!handled)
        {
            return null;
        }

        var token = request.Headers.Authorization?.Parameter;
        if (token is not ("tok-alice" or "tok-bob"))
        {
            return FakePaperless.Json(HttpStatusCode.Unauthorized, """{"detail":"Invalid token."}""");
        }

        var method = request.Method.Method;
        switch (path)
        {
            case "/api/saved_views/" when method == "GET":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":3,"next":null,"previous":null,"all":[2,3,5],"results":[
                     {"id":2,"name":"Factures","icon":"funnel","sort_field":"created","sort_reverse":true,
                      "filter_rules":[{"rule_type":4,"value":"1"}],"page_size":null,"display_mode":null,"display_fields":null,
                      "owner":1,"user_can_change":true,"permissions":{"view":{"users":[],"groups":[]},"change":{"users":[],"groups":[]}}},
                     {"id":3,"name":"Impôts 2026","sort_field":null,"sort_reverse":false,"filter_rules":[],"owner":2,"user_can_change":false},
                     {"id":5,"name":"Boîte de réception","sort_field":"added","sort_reverse":true,
                      "filter_rules":[{"rule_type":6,"value":"1"}],"owner":1,"user_can_change":true}]}
                    """);

            case "/api/saved_views/" when method == "POST":
                var created = JsonNode.Parse(body!)!.AsObject();
                created["id"] = 9;
                created["owner"] = 1;
                created["user_can_change"] = true;
                created["icon"] = "funnel";
                return FakePaperless.Json(HttpStatusCode.Created, created.ToJsonString());

            case "/api/saved_views/3/" when method is "PATCH" or "DELETE":
                return FakePaperless.Json(HttpStatusCode.Forbidden, """{"detail":"You do not have permission to perform this action."}""");

            case "/api/saved_views/2/" when method is "PATCH" or "GET":
                var view = JsonNode.Parse("""
                    {"id":2,"name":"Factures","icon":"funnel","sort_field":"created","sort_reverse":true,
                     "filter_rules":[{"rule_type":4,"value":"1"}],"owner":1,"user_can_change":true}
                    """)!.AsObject();
                foreach (var (key, value) in JsonNode.Parse(body ?? "{}")!.AsObject())
                {
                    view[key] = value?.DeepClone();
                }

                return FakePaperless.Json(HttpStatusCode.OK, view.ToJsonString());

            case "/api/saved_views/2/" when method == "DELETE":
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            case "/api/statistics/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"documents_total":120,"documents_inbox":4,"inbox_tag":1,"inbox_tags":[1],"document_file_type_counts":[{"mime_type":"application/pdf","mime_type_count":118}],
                     "character_count":456789,"tag_count":12,"correspondent_count":30,"document_type_count":8,"storage_path_count":2,"current_asn":57}
                    """);

            case "/api/custom_fields/" when token == "tok-bob":
                return FakePaperless.Json(HttpStatusCode.OK, """{"count":1,"results":[{"id":4,"name":"échéance","data_type":"string"}]}""");

            case "/api/custom_fields/":
                return FakePaperless.Json(HttpStatusCode.OK, """{"count":1,"results":[{"id":6,"name":"Échéance","data_type":"date"}]}""");

            case "/api/documents/":
                return FakePaperless.Json(HttpStatusCode.OK, """
                    {"count":7,"next":"http://paperless:8000/api/documents/?page=2","results":[
                     {"id":31,"title":"Assurance auto","correspondent":4,"tags":[2],"content":"secret","owner":1,
                      "custom_fields":[{"field":2,"value":"x"},{"field":6,"value":"2026-12-01"}]}]}
                    """);

            default:
                return FakePaperless.Json(HttpStatusCode.NotFound, """{"detail":"Not found."}""");
        }
    }
}

public sealed class SavedViewEndpointsTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
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

    /// <summary>Requêtes envoyées à Paperless depuis <paramref name="from"/> (hors connexion).</summary>
    private List<(HttpMethod Method, string PathAndQuery, string? Body)> Since(int from) =>
        [.. fixture.Paperless.History.Skip(from)];

    private static int[] Ints(JsonNode? node) => [.. node!.AsArray().Select(n => n!.GetValue<int>())];

    [Fact]
    public async Task Sans_session_401()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/vues", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Modification_sans_en_tete_anti_falsification_refusee()
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Post, "/api/vues", cookie, """{"name":"x"}""", antiForgery: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Liste_avec_tableau_de_bord_et_menu_dans_l_ordre_choisi()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Get, "/api/vues", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var views = json["vues"]!.AsArray();
        Assert.Equal(3, views.Count);
        var first = views[0]!.AsObject();
        Assert.Equal("Factures", first["name"]!.GetValue<string>());
        Assert.Equal(4, first["filter_rules"]![0]!["rule_type"]!.GetValue<int>());
        Assert.False(first.ContainsKey("permissions"));
        Assert.False(first.ContainsKey("icon"));
        Assert.False(views[1]!["user_can_change"]!.GetValue<bool>());
        // Ordre enregistré (5, puis 3 absent des visibles), puis les autres visibles ; la vue 99 n'existe plus.
        Assert.Equal([5, 2], Ints(json["tableau_de_bord"]));
        Assert.Equal([5], Ints(json["menu"]));
    }

    [Fact]
    public async Task Liste_sans_preferences()
    {
        var cookie = await LoginAsync("bob", "123456");

        using var response = await SendAsync(HttpMethod.Get, "/api/vues", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Empty(json["tableau_de_bord"]!.AsArray());
        Assert.Empty(json["menu"]!.AsArray());
    }

    [Fact]
    public async Task Creation_avec_affichage_enregistre_dans_les_preferences()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Post, "/api/vues", cookie, """
            {"name":" Assurances ","filter_rules":[{"rule_type":6,"value":"3"},{"rule_type":3,"value":null}],
             "sort_field":"created","sort_reverse":true,"tableau_de_bord":true,"menu":false}
            """);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var calls = Since(from);
        Assert.Equal(3, calls.Count);

        Assert.Equal((HttpMethod.Post, "/api/saved_views/"), (calls[0].Method, calls[0].PathAndQuery));
        var sent = JsonNode.Parse(calls[0].Body!)!.AsObject();
        Assert.Equal("Assurances", sent["name"]!.GetValue<string>());
        Assert.False(sent.ContainsKey("tableau_de_bord"));
        Assert.False(sent.ContainsKey("menu"));
        Assert.Null(sent["filter_rules"]![1]!["value"]);

        Assert.Equal(HttpMethod.Get, calls[1].Method);
        Assert.Equal((HttpMethod.Post, "/api/ui_settings/"), (calls[2].Method, calls[2].PathAndQuery));
        var settings = JsonNode.Parse(calls[2].Body!)!["settings"]!.AsObject();
        Assert.Equal([2, 5, 9, 99], Ints(settings["saved_views"]!["dashboard_views_visible_ids"]));
        Assert.Equal([5], Ints(settings["saved_views"]!["sidebar_views_visible_ids"]));
        // Les autres préférences sont conservées ; la configuration du serveur n'est pas réécrite.
        Assert.True(settings["saved_views"]!["warn_on_unsaved_change"]!.GetValue<bool>());
        Assert.Equal("true", settings["dark_mode"]!["enabled"]!.GetValue<string>());
        Assert.False(settings.ContainsKey("trash_delay"));
        Assert.False(settings.ContainsKey("gmail_oauth_url"));

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(9, json["id"]!.GetValue<int>());
        Assert.False(json.ContainsKey("icon"));
    }

    [Fact]
    public async Task Creation_sans_affichage_ne_touche_pas_aux_preferences()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Post, "/api/vues", cookie, """{"name":"Sans règle"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var calls = Since(from);
        Assert.Single(calls);
        Assert.Empty(JsonNode.Parse(calls[0].Body!)!["filter_rules"]!.AsArray());
    }

    [Theory]
    [InlineData("""{"filter_rules":[]}""")]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"x","filter_rules":[{"rule_type":51,"value":"1"}]}""")]
    [InlineData("""{"name":"x","filter_rules":[{"rule_type":"6","value":"1"}]}""")]
    [InlineData("""{"name":"x","filter_rules":[{"rule_type":6,"value":1}]}""")]
    [InlineData("""{"name":"x","filter_rules":[{"rule_type":6,"value":"1","autre":true}]}""")]
    [InlineData("""{"name":"x","filter_rules":{"rule_type":6}}""")]
    [InlineData("""{"name":"x","sort_field":"-created"}""")]
    [InlineData("""{"name":"x","sort_field":"created;drop"}""")]
    [InlineData("""{"name":"x","sort_reverse":"oui"}""")]
    [InlineData("""{"name":"x","menu":1}""")]
    [InlineData("""{"name":"x","owner":2}""")]
    [InlineData("""{"name":"x","set_permissions":{}}""")]
    public async Task Creation_invalide_refusee_sans_appeler_Paperless(string body)
    {
        var cookie = await LoginAsync();
        var calls = fixture.Paperless.Calls;

        using var response = await SendAsync(HttpMethod.Post, "/api/vues", cookie, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("requete_invalide", await ErrorCodeAsync(response));
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public void Trop_de_regles_refusees()
    {
        var rules = new JsonArray([.. Enumerable.Range(0, SavedViewEndpoints.MaxRules + 1)
            .Select(_ => (JsonNode)new JsonObject { ["rule_type"] = 6, ["value"] = "1" })]);

        var (view, _, error) = SavedViewEndpoints.Sanitize(new JsonObject { ["name"] = "x", ["filter_rules"] = rules }, creating: true);

        Assert.Null(view);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Modification_des_criteres_et_retrait_du_tableau_de_bord()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Patch, "/api/vues/2", cookie, """
            {"filter_rules":[{"rule_type":22,"value":"3"}],"sort_field":"title","sort_reverse":false,"tableau_de_bord":false,"menu":true}
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calls = Since(from);
        Assert.Equal((HttpMethod.Patch, "/api/saved_views/2/"), (calls[0].Method, calls[0].PathAndQuery));
        var settings = JsonNode.Parse(calls[^1].Body!)!["settings"]!;
        Assert.Equal([5, 99], Ints(settings["saved_views"]!["dashboard_views_visible_ids"]));
        Assert.Equal([2, 5], Ints(settings["saved_views"]!["sidebar_views_visible_ids"]));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("title", json["sort_field"]!.GetValue<string>());
    }

    [Fact]
    public async Task Affichage_seul_verifie_que_la_vue_existe_sans_la_modifier()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Patch, "/api/vues/2", cookie, """{"menu":true}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calls = Since(from);
        Assert.Equal((HttpMethod.Get, "/api/saved_views/2/"), (calls[0].Method, calls[0].PathAndQuery));
        Assert.DoesNotContain(calls, c => c.Method == HttpMethod.Patch);
        Assert.Equal(HttpMethod.Post, calls[^1].Method);
    }

    [Fact]
    public async Task Modification_refusee_par_Paperless_sans_toucher_aux_preferences()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Patch, "/api/vues/3", cookie, """{"name":"x","menu":true}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("acces_refuse", await ErrorCodeAsync(response));
        Assert.Single(Since(from));
    }

    [Fact]
    public async Task Suppression()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Delete, "/api/vues/2", cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("/api/saved_views/2/", fixture.Paperless.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Vue_introuvable()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Delete, "/api/vues/42", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("introuvable", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Statistiques_filtrees()
    {
        var cookie = await LoginAsync();

        using var response = await SendAsync(HttpMethod.Get, "/api/tableau-de-bord/statistiques", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(120, json["documents_total"]!.GetValue<int>());
        Assert.Equal(4, json["documents_inbox"]!.GetValue<int>());
        Assert.False(json.ContainsKey("current_asn"));
        Assert.False(json.ContainsKey("inbox_tag"));
    }

    [Fact]
    public async Task Echeances_a_partir_d_aujourd_hui_la_plus_proche_d_abord()
    {
        var cookie = await LoginAsync();
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Get, "/api/tableau-de-bord/echeances?nombre=500", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calls = Since(from);
        var lookup = QueryHelpers.ParseQuery(new Uri("http://x" + calls[0].PathAndQuery).Query);
        Assert.Equal(DashboardEndpoints.DeadlineFieldName, lookup["name__iexact"]);
        var query = QueryHelpers.ParseQuery(new Uri("http://x" + calls[1].PathAndQuery).Query);
        var today = DateOnly.FromDateTime(fixture.Time.GetLocalNow().DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"[6,\"gte\",\"{today}\"]", query["custom_field_query"]);
        Assert.Equal("custom_field_6", query["ordering"]);
        Assert.Equal(DashboardEndpoints.MaxDeadlineCount.ToString(System.Globalization.CultureInfo.InvariantCulture), query["page_size"]);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("paperless:8000", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
        var json = JsonNode.Parse(body)!;
        Assert.Equal(6, json["champ"]!["id"]!.GetValue<int>());
        Assert.Equal(7, json["count"]!.GetValue<int>());
        var item = json["results"]![0]!;
        Assert.Equal("Assurance auto", item["title"]!.GetValue<string>());
        Assert.Equal("2026-12-01", item["echeance"]!.GetValue<string>());
    }

    [Fact]
    public async Task Echeances_sans_champ_date_reponse_vide()
    {
        var cookie = await LoginAsync("bob", "123456");
        var from = fixture.Paperless.History.Count;

        using var response = await SendAsync(HttpMethod.Get, "/api/tableau-de-bord/echeances", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Since(from));
        Assert.Equal("""{"champ":null,"count":0,"results":[]}""", await response.Content.ReadAsStringAsync());
    }
}
