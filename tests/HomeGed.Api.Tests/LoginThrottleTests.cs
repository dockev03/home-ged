using HomeGed.Api.Auth;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Tests;

public sealed class LoginThrottleTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly LoginThrottle _throttle;

    public LoginThrottleTests() =>
        _throttle = new LoginThrottle(Options.Create(new AuthOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 }), _time);

    private void Fail(int times, params string[] keys)
    {
        for (var i = 0; i < times; i++)
        {
            _throttle.RecordFailure(keys);
        }
    }

    [Fact]
    public void Bloque_au_cinquieme_echec_pendant_15_minutes()
    {
        Fail(4, "ip:1");
        Assert.Null(_throttle.GetRetryAfter("ip:1"));

        Fail(1, "ip:1");
        Assert.Equal(TimeSpan.FromMinutes(15), _throttle.GetRetryAfter("ip:1"));

        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(TimeSpan.FromMinutes(1), _throttle.GetRetryAfter("ip:1"));

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(_throttle.GetRetryAfter("ip:1"));
    }

    [Fact]
    public void Les_echecs_anciens_sortent_de_la_fenetre()
    {
        Fail(4, "ip:1");
        _time.Advance(TimeSpan.FromMinutes(16));
        Fail(4, "ip:1");
        Assert.Null(_throttle.GetRetryAfter("ip:1"));
    }

    [Fact]
    public void Compte_par_ip_et_par_identifiant_separement()
    {
        Fail(5, LoginThrottle.IpKey("10.0.0.1"), LoginThrottle.UserKey("Alice"));

        Assert.NotNull(_throttle.GetRetryAfter(LoginThrottle.IpKey("10.0.0.2"), LoginThrottle.UserKey(" alice ")));
        Assert.NotNull(_throttle.GetRetryAfter(LoginThrottle.IpKey("10.0.0.1"), LoginThrottle.UserKey("bob")));
        Assert.Null(_throttle.GetRetryAfter(LoginThrottle.IpKey("10.0.0.2"), LoginThrottle.UserKey("bob")));
    }

    [Fact]
    public void Reset_efface_les_echecs()
    {
        Fail(4, "user:A");
        _throttle.Reset("user:A");
        Fail(4, "user:A");
        Assert.Null(_throttle.GetRetryAfter("user:A"));
    }

    [Fact]
    public void Purge_les_entrees_perimees_au_dela_du_seuil()
    {
        for (var i = 0; i <= LoginThrottle.SweepThreshold; i++)
        {
            _throttle.RecordFailure("user:" + i);
        }

        _time.Advance(TimeSpan.FromMinutes(16));
        _throttle.RecordFailure("user:nouveau");

        Assert.Equal(1, _throttle.Count);
    }
}
