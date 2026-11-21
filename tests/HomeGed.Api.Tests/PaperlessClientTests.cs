using System.Net;
using HomeGed.Api.Paperless;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeGed.Api.Tests;

public sealed class PaperlessClientTests
{
    private static (PaperlessClient Client, HttpClient Http) Create(FakeHttpHandler handler)
    {
        var http = new HttpClient(handler);
        PaperlessClient.Configure(http, new PaperlessOptions { BaseUrl = "http://paperless:8000", ApiVersion = 10 });
        return (new PaperlessClient(http, NullLogger<PaperlessClient>.Instance), http);
    }

    [Fact]
    public async Task Fige_la_version_d_api_dans_l_en_tete_Accept()
    {
        var handler = FakeHttpHandler.Returning(HttpStatusCode.OK);
        var (client, http) = Create(handler);
        using (http)
        {
            await client.ProbeAsync(CancellationToken.None);
        }

        var accept = Assert.Single(handler.LastRequest!.Headers.Accept);
        Assert.Equal("application/json", accept.MediaType);
        Assert.Contains(accept.Parameters, p => p.Name == "version" && p.Value == "10");
        Assert.Equal(new Uri("http://paperless:8000/api/documents/?page_size=1"), handler.LastRequest.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PaperlessProbeResult.Ok)]
    [InlineData(HttpStatusCode.OK, PaperlessProbeResult.Ok)]
    [InlineData(HttpStatusCode.NotAcceptable, PaperlessProbeResult.ApiVersionRejected)]
    public async Task Interprete_la_reponse_de_Paperless(HttpStatusCode status, PaperlessProbeResult expected)
    {
        var (client, http) = Create(FakeHttpHandler.Returning(status));
        using (http)
        {
            Assert.Equal(expected, await client.ProbeAsync(CancellationToken.None));
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Found)]
    public async Task Signale_une_reponse_inattendue(HttpStatusCode status)
    {
        var (client, http) = Create(FakeHttpHandler.Returning(status));
        using (http)
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.ProbeAsync(CancellationToken.None));
            Assert.Equal(status, ex.StatusCode);
        }
    }
}
