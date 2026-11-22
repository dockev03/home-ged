using HomeGed.Api.Auth;

namespace HomeGed.Api.Tests;

public sealed class SessionTokensTests
{
    [Fact]
    public void Jeton_aleatoire_de_256_bits_et_empreinte_stable()
    {
        var token = SessionTokens.NewToken();

        Assert.Equal(43, token.Length);
        Assert.NotEqual(token, SessionTokens.NewToken());
        Assert.Equal(SessionTokens.Hash(token), SessionTokens.Hash(token));
        Assert.Equal(32, SessionTokens.Hash(token)!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas un jeton")]
    [InlineData("YWJj")]
    public void Jeton_mal_forme_sans_empreinte(string? token) => Assert.Null(SessionTokens.Hash(token));

    [Fact]
    public void Echeance_glissante_plafonnee_par_la_duree_maximale()
    {
        var options = new AuthOptions { IdleTimeoutDays = 30, AbsoluteLifetimeDays = 90 };
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(created.AddDays(30), SessionAuthenticationHandler.ComputeExpiry(created, created, options));
        Assert.Equal(created.AddDays(80), SessionAuthenticationHandler.ComputeExpiry(created, created.AddDays(50), options));
        Assert.Equal(created.AddDays(90), SessionAuthenticationHandler.ComputeExpiry(created, created.AddDays(70), options));
    }
}
