using System.Net;
using System.Text.Json;
using HomeGed.Api.Paperless;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeGed.Api.Tests;

/// <summary>Demande de token : réponses observées sur Paperless 3.5.1 (docs/poc/auth-2fa.md).</summary>
public sealed class PaperlessAuthTests
{
    private static async Task<(TokenResult Result, FakeHttpHandler Handler)> RequestAsync(
        FakeHttpHandler handler, string? code = null, string? ip = "203.0.113.7")
    {
        using var http = new HttpClient(handler);
        PaperlessClient.Configure(http, new PaperlessOptions { BaseUrl = "http://paperless:8000" });
        var client = new PaperlessClient(http, NullLogger<PaperlessClient>.Instance);
        return (await client.RequestTokenAsync("alice", "secret", code, ip, CancellationToken.None), handler);
    }

    [Fact]
    public async Task Envoie_identifiants_code_et_ip_du_client()
    {
        var (result, handler) = await RequestAsync(
            FakeHttpHandler.Returning(HttpStatusCode.OK, """{"token":"abc"}"""), code: "123456");

        Assert.Equal(new TokenResult(TokenOutcome.Success, "abc"), result);
        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://paperless:8000/api/token/"), request.RequestUri);
        Assert.Equal("203.0.113.7", Assert.Single(request.Headers.GetValues("X-Forwarded-For")));
        // Longueur connue : Paperless ignore les corps « chunked ».
        Assert.NotNull(request.Content!.Headers.ContentLength);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("alice", body.RootElement.GetProperty("username").GetString());
        Assert.Equal("secret", body.RootElement.GetProperty("password").GetString());
        Assert.Equal("123456", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task N_envoie_pas_de_code_vide()
    {
        var (_, handler) = await RequestAsync(FakeHttpHandler.Returning(HttpStatusCode.OK, """{"token":"abc"}"""), code: "");
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.False(body.RootElement.TryGetProperty("code", out _));
    }

    [Theory]
    [InlineData("""{"non_field_errors":["MFA code is required"]}""", TokenOutcome.MfaRequired)]
    [InlineData("""{"non_field_errors":["Invalid MFA code"]}""", TokenOutcome.InvalidMfaCode)]
    [InlineData("""{"non_field_errors":["Unable to log in with provided credentials."]}""", TokenOutcome.InvalidCredentials)]
    [InlineData("pas du json", TokenOutcome.InvalidCredentials)]
    public async Task Interprete_les_refus(string json, TokenOutcome expected)
    {
        var (result, _) = await RequestAsync(FakeHttpHandler.Returning(HttpStatusCode.BadRequest, json));
        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Remonte_la_limite_de_Paperless()
    {
        var (result, _) = await RequestAsync(FakeHttpHandler.Returning(
            HttpStatusCode.TooManyRequests, """{"detail":"throttled"}""", ("Retry-After", "42")));
        Assert.Equal(new TokenResult(TokenOutcome.Throttled, RetryAfter: TimeSpan.FromSeconds(42)), result);
    }

    [Fact]
    public async Task Reponse_inattendue_leve_une_erreur()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            RequestAsync(FakeHttpHandler.Returning(HttpStatusCode.BadGateway)));
    }

    [Fact]
    public async Task Lit_l_utilisateur_du_token()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK,
            """{"user":{"id":2,"username":"admin","is_staff":true},"settings":{}}""");
        using var http = new HttpClient(handler);
        PaperlessClient.Configure(http, new PaperlessOptions { BaseUrl = "http://paperless:8000" });
        var client = new PaperlessClient(http, NullLogger<PaperlessClient>.Instance);

        var user = await client.GetCurrentUserAsync("abc", CancellationToken.None);

        Assert.Equal(new PaperlessUser(2, "admin"), user);
        Assert.Equal("Token abc", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal(new Uri("http://paperless:8000/api/ui_settings/"), handler.LastRequest.RequestUri);
    }
}
