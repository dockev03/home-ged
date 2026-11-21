using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HomeGed.Api.Tests;

/// <summary>Démarre la vraie application sur un port libre (sans paquet de test supplémentaire).</summary>
public sealed class AppFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public HttpClient Http { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Paperless pointe sur un port fermé : il est volontairement injoignable.
        _app = HomeGedApp.Create(["--Paperless:BaseUrl=http://127.0.0.1:9", "--Paperless:TimeoutSeconds=2"],
            b => b.WebHost.UseUrls("http://127.0.0.1:0"));
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Http = new HttpClient { BaseAddress = new Uri(address) };
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

public sealed class AppTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task Health_repond_sans_dependre_de_Paperless()
    {
        using var response = await fixture.Http.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ready_echoue_si_Paperless_est_injoignable()
    {
        using var response = await fixture.Http.GetAsync(new Uri("/health/ready", UriKind.Relative));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Ajoute_les_en_tetes_de_securite()
    {
        using var response = await fixture.Http.GetAsync(new Uri("/health", UriKind.Relative));
        var headers = response.Headers;
        Assert.Contains("frame-ancestors 'none'", headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", headers.GetValues("X-Frame-Options").Single());
        Assert.False(headers.Contains("Server"));
    }

    [Fact]
    public async Task Route_api_inconnue_renvoie_404_json()
    {
        using var response = await fixture.Http.GetAsync(new Uri("/api/inexistant", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Api_info_renvoie_la_version()
    {
        var json = await fixture.Http.GetStringAsync(new Uri("/api/info", UriKind.Relative));
        using var doc = JsonDocument.Parse(json);
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("version").GetString()));
    }

    [Fact]
    public void Configuration_invalide_bloque_le_demarrage()
    {
        var app = HomeGedApp.Create(["--Paperless:BaseUrl="], b => b.WebHost.UseUrls("http://127.0.0.1:0"));
        var ex = Assert.ThrowsAny<Exception>(() => app.StartAsync().GetAwaiter().GetResult());
        Assert.Contains("BaseUrl", ex.Message, StringComparison.Ordinal);
    }
}
