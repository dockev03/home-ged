using HomeGed.Api.Paperless;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeGed.IntegrationTests;

/// <summary>
/// Tests contre une instance Paperless réelle (§3.4.3), lancée par la CI.
/// Variables : voir <see cref="IntegrationEnvironment"/>.
/// </summary>
public sealed class PaperlessIntegrationTests
{
    private static PaperlessClient CreateClient(out HttpClient http, int apiVersion)
    {
        http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        PaperlessClient.Configure(http, new PaperlessOptions { BaseUrl = IntegrationEnvironment.PaperlessUrl, ApiVersion = apiVersion });
        return new PaperlessClient(http, NullLogger<PaperlessClient>.Instance);
    }

    [Fact]
    public async Task Paperless_accepte_la_version_d_api_figee()
    {
        var client = CreateClient(out var http, apiVersion: 10);
        using (http)
        {
            Assert.Equal(PaperlessProbeResult.Ok, await client.ProbeAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Paperless_refuse_une_version_d_api_inconnue()
    {
        var client = CreateClient(out var http, apiVersion: 99);
        using (http)
        {
            Assert.Equal(PaperlessProbeResult.ApiVersionRejected, await client.ProbeAsync(CancellationToken.None));
        }
    }
}
