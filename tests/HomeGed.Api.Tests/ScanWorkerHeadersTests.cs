using System.Net;
using HomeGed.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HomeGed.Api.Tests;

/// <summary>Application servant un front factice : page, script ordinaire et worker de recadrage.</summary>
public sealed class StaticFrontFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "homeged-front-" + Guid.NewGuid().ToString("N"));

    public HttpClient Http { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), "<!doctype html>");
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "index-abc123.js"), "1");
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "opencv.worker-abc123.js"), "1");
        _app = HomeGedApp.Create(
            [
                "--webroot=" + _webRoot,
                "--Paperless:BaseUrl=http://127.0.0.1:9", "--Paperless:TimeoutSeconds=2",
                "--Database:ConnectionString=Host=127.0.0.1;Port=9;Timeout=2", "--Database:MigrateOnStartup=false",
            ],
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

        Directory.Delete(_webRoot, recursive: true);
    }
}

public sealed class ScanWorkerHeadersTests(StaticFrontFixture fixture) : IClassFixture<StaticFrontFixture>
{
    private async Task<(HttpStatusCode Status, string Csp)> GetAsync(string path)
    {
        using var response = await fixture.Http.GetAsync(new Uri(path, UriKind.Relative));
        return (response.StatusCode, response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Le_worker_de_recadrage_recoit_sa_propre_CSP()
    {
        var (status, csp) = await GetAsync("/assets/opencv.worker-abc123.js");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(SecurityHeaders.ScanWorkerContentSecurityPolicy, csp);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/scanner")]
    [InlineData("/assets/index-abc123.js")]
    // Fichier absent : le repli sert index.html, qui ne doit jamais hériter de la CSP du worker.
    [InlineData("/assets/opencv.worker-absent.js")]
    public async Task Les_autres_reponses_gardent_la_CSP_stricte(string path)
    {
        var (_, csp) = await GetAsync(path);

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, csp);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/assets/opencv.worker-abc.js", true)]
    [InlineData("/assets/opencv.worker-abc.css", false)]
    [InlineData("/assets/opencv.worker-abc/autre.js", false)]
    [InlineData("/assets/opencv-abc.js", false)]
    [InlineData("/autre/assets/opencv.worker-abc.js", false)]
    public void Seul_le_script_du_worker_est_concerne(string path, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Headers.ContentSecurityPolicy = SecurityHeaders.ContentSecurityPolicy;

        SecurityHeaders.ApplyScanWorkerPolicy(context);

        Assert.Equal(expected, context.Response.Headers.ContentSecurityPolicy == SecurityHeaders.ScanWorkerContentSecurityPolicy);
    }
}
