using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeGed.Api.Auth;
using HomeGed.Api.Data;
using HomeGed.Api.Paperless;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HomeGed.Api.Tests;

/// <summary>
/// Application réelle avec un faux Paperless, des sessions en mémoire et une horloge pilotée.
/// Comptes : alice/secret (sans 2FA), bob/secret (2FA, code 123456).
/// </summary>
public sealed class AuthAppFixture : IAsyncLifetime
{
    private WebApplication? _app;

    internal InMemorySessionStore Store { get; } = new();

    internal FakeTimeProvider Time { get; } = new();

    internal FakeHttpHandler Paperless { get; }

    /// <summary>X-Forwarded-For reçu par le faux Paperless sur la dernière demande de token.</summary>
    internal string? LastTokenForwardedFor { get; private set; }

    public AuthAppFixture() =>
        Paperless = new((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/token/")
            {
                LastTokenForwardedFor = request.Headers.TryGetValues("X-Forwarded-For", out var v) ? string.Join(",", v) : null;
            }

            return FakePaperless.Respond(request, body);
        });

    internal IServiceProvider Services => _app!.Services;

    public HttpClient Http { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _app = HomeGedApp.Create(
            [
                "--Paperless:BaseUrl=http://paperless:8000",
                "--Database:ConnectionString=Host=127.0.0.1;Port=9",
                "--Database:MigrateOnStartup=false",
            ],
            b =>
            {
                b.WebHost.UseUrls("http://127.0.0.1:0");
                b.Services.AddHttpClient<PaperlessClient>().ConfigurePrimaryHttpMessageHandler(() => Paperless);
                b.Services.AddSingleton<ISessionStore>(Store);
                b.Services.AddSingleton<TimeProvider>(Time);
                b.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            });
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        // Cookies gérés à la main : le cookie __Host- est Secure et les tests sont en HTTP.
        Http = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        Http.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

internal static class FakePaperless
{
    public static HttpResponseMessage Respond(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/token/")
        {
            using var json = JsonDocument.Parse(body!);
            var root = json.RootElement;
            var user = root.GetProperty("username").GetString();
            var password = root.GetProperty("password").GetString();
            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (user == "panne")
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            if (user == "limite")
            {
                var r = Json(HttpStatusCode.TooManyRequests, """{"detail":"throttled"}""");
                r.Headers.TryAddWithoutValidation("Retry-After", "30");
                return r;
            }

            if (user is not ("alice" or "bob") || password != "secret")
            {
                return Json(HttpStatusCode.BadRequest, """{"non_field_errors":["Unable to log in with provided credentials."]}""");
            }

            if (user == "bob" && code is null)
            {
                return Json(HttpStatusCode.BadRequest, """{"non_field_errors":["MFA code is required"]}""");
            }

            if (user == "bob" && code != "123456")
            {
                return Json(HttpStatusCode.BadRequest, """{"non_field_errors":["Invalid MFA code"]}""");
            }

            return Json(HttpStatusCode.OK, $$"""{"token":"tok-{{user}}"}""");
        }

        if (path == "/api/ui_settings/")
        {
            return request.Headers.Authorization?.Parameter switch
            {
                "tok-alice" => Json(HttpStatusCode.OK, """{"user":{"id":1,"username":"alice"}}"""),
                "tok-bob" => Json(HttpStatusCode.OK, """{"user":{"id":2,"username":"bob"}}"""),
                _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            };
        }

        return FakePaperlessDocuments.Respond(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    internal static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

public sealed class AuthEndpointsTests(AuthAppFixture fixture) : IClassFixture<AuthAppFixture>
{
    private static int _nextIp;

    /// <summary>IP distincte par test (X-Forwarded-For accepté depuis la boucle locale) : compteurs indépendants.</summary>
    private readonly string _ip = $"198.51.100.{Interlocked.Increment(ref _nextIp)}";

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body = null, string? cookie = null, bool antiForgery = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", _ip);
        if (antiForgery)
        {
            request.Headers.TryAddWithoutValidation(AntiForgery.HeaderName, "HomeGed");
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{SessionTokens.CookieName}={cookie}");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await fixture.Http.SendAsync(request);
    }

    private Task<HttpResponseMessage> LoginAsync(string username, string password = "secret", string? code = null) =>
        SendAsync(HttpMethod.Post, "/api/auth/login", new { username, password, code });

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string SessionCookie(HttpResponseMessage response)
    {
        var header = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith(SessionTokens.CookieName + "=", StringComparison.Ordinal));
        return header.Split(';')[0][(SessionTokens.CookieName.Length + 1)..];
    }

    [Fact]
    public async Task Connexion_cree_une_session_et_un_cookie_securise()
    {
        using var response = await LoginAsync("alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).ToUpperInvariant();
        Assert.Contains("HTTPONLY", setCookie, StringComparison.Ordinal);
        Assert.Contains("SECURE", setCookie, StringComparison.Ordinal);
        Assert.Contains("SAMESITE=STRICT", setCookie, StringComparison.Ordinal);
        Assert.Contains("PATH=/", setCookie, StringComparison.Ordinal);
        Assert.Equal("alice", (await response.Content.ReadFromJsonAsync<CurrentUser>())!.Username);

        var cookie = SessionCookie(response);
        var session = fixture.Store.Sessions.Values.Single(s => s.TokenHash.AsSpan().SequenceEqual(SessionTokens.Hash(cookie)));
        Assert.Equal(1, session.UserId);
        Assert.Equal(_ip, session.Ip);
        Assert.Equal(fixture.Time.GetUtcNow().AddDays(30), session.ExpiresAt);
        // Le token Paperless est chiffré au repos, et récupérable par le BFF.
        Assert.DoesNotContain("tok-alice", session.PaperlessToken, StringComparison.Ordinal);
        Assert.Equal("tok-alice", fixture.Services.GetRequiredService<PaperlessTokenProtector>().Unprotect(session.PaperlessToken));

        using var me = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie: cookie);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(new CurrentUser(1, "alice"), await me.Content.ReadFromJsonAsync<CurrentUser>());
    }

    [Fact]
    public async Task Transmet_l_ip_du_client_a_Paperless()
    {
        using var response = await LoginAsync("alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Remplace la valeur reçue : la limite de Paperless s'applique par appareil.
        Assert.Equal(_ip, fixture.LastTokenForwardedFor);
    }

    [Fact]
    public async Task Compte_avec_2FA_demande_puis_accepte_le_code()
    {
        using var sansCode = await LoginAsync("bob");
        Assert.Equal(HttpStatusCode.Unauthorized, sansCode.StatusCode);
        Assert.Equal("mfa_requis", await ErrorCodeAsync(sansCode));
        Assert.False(sansCode.Headers.Contains("Set-Cookie"));

        using var codeFaux = await LoginAsync("bob", code: "000000");
        Assert.Equal("code_invalide", await ErrorCodeAsync(codeFaux));

        using var ok = await LoginAsync("bob", code: "123 456");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Mauvais_mot_de_passe_refuse()
    {
        using var response = await LoginAsync("alice", "faux");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("identifiants_invalides", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("", "secret", null)]
    [InlineData("alice", "", null)]
    [InlineData("alice", "secret", "12ab56")]
    public async Task Requete_invalide_refusee_sans_appeler_Paperless(string username, string password, string? code)
    {
        var calls = fixture.Paperless.Calls;
        using var response = await LoginAsync(username, password, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Cinq_echecs_bloquent_l_ip_avant_d_appeler_Paperless()
    {
        for (var i = 0; i < 5; i++)
        {
            using var echec = await LoginAsync("inconnu" + i, "faux");
            Assert.Equal(HttpStatusCode.Unauthorized, echec.StatusCode);
        }

        var calls = fixture.Paperless.Calls;
        using var bloque = await LoginAsync("alice");

        Assert.Equal(HttpStatusCode.TooManyRequests, bloque.StatusCode);
        Assert.Equal("trop_de_tentatives", await ErrorCodeAsync(bloque));
        Assert.Equal(TimeSpan.FromMinutes(15), bloque.Headers.RetryAfter!.Delta);
        Assert.Equal(calls, fixture.Paperless.Calls);
    }

    [Fact]
    public async Task Relaie_la_limite_de_Paperless()
    {
        using var response = await LoginAsync("limite");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.RetryAfter!.Delta);
    }

    [Fact]
    public async Task Paperless_en_panne_renvoie_503()
    {
        using var response = await LoginAsync("panne");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("paperless_indisponible", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Requete_sans_en_tete_anti_CSRF_refusee()
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/auth/login", new { username = "alice", password = "secret" }, antiForgery: false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("n-importe-quoi")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Api_protegee_sans_session_valide_renvoie_401(string? cookie)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie: cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("non_connecte", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Deconnexion_revoque_la_session()
    {
        using var login = await LoginAsync("alice");
        var cookie = SessionCookie(login);

        using var logout = await SendAsync(HttpMethod.Post, "/api/auth/logout", cookie: cookie);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("EXPIRES=THU, 01 JAN 1970", Assert.Single(logout.Headers.GetValues("Set-Cookie")).ToUpperInvariant(), StringComparison.Ordinal);

        using var me = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie: cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Liste_et_revoque_ses_sessions_mais_pas_celles_des_autres()
    {
        using var login1 = await LoginAsync("alice");
        using var login2 = await LoginAsync("alice");
        using var loginBob = await LoginAsync("bob", code: "123456");
        var (cookie1, cookie2) = (SessionCookie(login1), SessionCookie(login2));
        var bobSession = fixture.Store.Sessions.Values.First(s => s.UserId == 2 && s.RevokedAt is null);

        using var list = await SendAsync(HttpMethod.Get, "/api/auth/sessions", cookie: cookie1);
        var sessions = (await list.Content.ReadFromJsonAsync<List<SessionInfo>>())!;
        Assert.All(sessions, s => Assert.DoesNotContain(fixture.Store.Sessions.Values, x => x.Id == s.Id && x.UserId != 1));
        var current = Assert.Single(sessions, s => s.Current);
        var other = fixture.Store.Sessions.Values.Single(s => s.TokenHash.AsSpan().SequenceEqual(SessionTokens.Hash(cookie2)));
        Assert.Contains(sessions, s => s.Id == other.Id && !s.Current);

        using var revokeBob = await SendAsync(HttpMethod.Delete, $"/api/auth/sessions/{bobSession.Id}", cookie: cookie1);
        Assert.Equal(HttpStatusCode.NotFound, revokeBob.StatusCode);
        Assert.Null(bobSession.RevokedAt);

        using var revoke = await SendAsync(HttpMethod.Delete, $"/api/auth/sessions/{other.Id}", cookie: cookie1);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var me2 = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie: cookie2);
        Assert.Equal(HttpStatusCode.Unauthorized, me2.StatusCode);
        using var me1 = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie: cookie1);
        Assert.Equal(HttpStatusCode.OK, me1.StatusCode);
        Assert.NotEqual(Guid.Empty, current.Id);
    }

    [Fact]
    public async Task L_expiration_glisse_avec_l_usage()
    {
        using var login = await LoginAsync("alice");
        var cookie = SessionCookie(login);
        var session = fixture.Store.Sessions.Values.Single(s => s.TokenHash.AsSpan().SequenceEqual(SessionTokens.Hash(cookie)));
        var initialExpiry = session.ExpiresAt;

        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        using var me = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie: cookie);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(initialExpiry.AddMinutes(10), session.ExpiresAt);
    }

    [Fact]
    public async Task Session_expiree_refusee()
    {
        using var login = await LoginAsync("alice");
        var cookie = SessionCookie(login);
        var session = fixture.Store.Sessions.Values.Single(s => s.TokenHash.AsSpan().SequenceEqual(SessionTokens.Hash(cookie)));
        session.ExpiresAt = fixture.Time.GetUtcNow();

        using var me = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie: cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Api_info_reste_publique()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
