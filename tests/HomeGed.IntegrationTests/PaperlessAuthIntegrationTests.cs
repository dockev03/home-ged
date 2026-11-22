using HomeGed.Api.Paperless;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeGed.IntegrationTests;

/// <summary>Demande de token et lecture de l'utilisateur sur un Paperless réel (compte sans 2FA).</summary>
public sealed class PaperlessAuthIntegrationTests
{
    private static PaperlessClient CreateClient(out HttpClient http)
    {
        http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        PaperlessClient.Configure(http, new PaperlessOptions { BaseUrl = IntegrationEnvironment.PaperlessUrl });
        return new PaperlessClient(http, NullLogger<PaperlessClient>.Instance);
    }

    [Fact]
    public async Task Obtient_le_token_puis_l_utilisateur()
    {
        var client = CreateClient(out var http);
        using (http)
        {
            var result = await client.RequestTokenAsync(
                IntegrationEnvironment.PaperlessUser, IntegrationEnvironment.PaperlessPassword, null, "198.51.100.10", CancellationToken.None);

            Assert.Equal(TokenOutcome.Success, result.Outcome);
            var user = await client.GetCurrentUserAsync(result.Token!, CancellationToken.None);
            Assert.Equal(IntegrationEnvironment.PaperlessUser, user.Username);
            Assert.True(user.Id > 0);
        }
    }

    [Fact]
    public async Task Mauvais_mot_de_passe_refuse()
    {
        var client = CreateClient(out var http);
        using (http)
        {
            var result = await client.RequestTokenAsync(
                IntegrationEnvironment.PaperlessUser, "mauvais-mot-de-passe", null, "198.51.100.11", CancellationToken.None);
            Assert.Equal(TokenOutcome.InvalidCredentials, result.Outcome);
        }
    }
}
