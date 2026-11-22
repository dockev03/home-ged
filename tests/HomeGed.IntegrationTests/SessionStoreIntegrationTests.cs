using System.Security.Cryptography;
using HomeGed.Api.Auth;
using HomeGed.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeGed.IntegrationTests;

/// <summary>Base homeged : migrations et stockage des sessions sur un PostgreSQL réel.</summary>
public sealed class SessionStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    private HomeGedDbContext _db = null!;

    private EfSessionStore _store = null!;

    public async Task InitializeAsync()
    {
        _db = new HomeGedDbContext(new DbContextOptionsBuilder<HomeGedDbContext>()
            .UseNpgsql(IntegrationEnvironment.HomeGedDb).Options);
        await _db.Database.MigrateAsync();
        await _db.Sessions.ExecuteDeleteAsync();
        _store = new EfSessionStore(_db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _db.Dispose();

    private static UserSession NewSession(int userId, DateTimeOffset? expiresAt = null) => new()
    {
        Id = Guid.CreateVersion7(),
        TokenHash = RandomNumberGenerator.GetBytes(32),
        UserId = userId,
        Username = "u" + userId,
        PaperlessToken = "chiffré",
        CreatedAt = Now,
        LastSeenAt = Now,
        ExpiresAt = expiresAt ?? Now.AddDays(30),
        Ip = "203.0.113.1",
        UserAgent = "test",
    };

    [Fact]
    public async Task Les_migrations_sont_toutes_appliquees()
    {
        Assert.Empty(await _db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Retrouve_une_session_active_par_empreinte()
    {
        var session = NewSession(1);
        await _store.AddAsync(session, CancellationToken.None);

        var found = await _store.FindActiveAsync(session.TokenHash, Now, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(session.Id, found.Id);
        Assert.Null(await _store.FindActiveAsync(RandomNumberGenerator.GetBytes(32), Now, CancellationToken.None));
        Assert.Null(await _store.FindActiveAsync(session.TokenHash, session.ExpiresAt, CancellationToken.None));
    }

    [Fact]
    public async Task Touch_prolonge_et_revoke_ne_touche_que_ses_sessions()
    {
        var mine = NewSession(1);
        await _store.AddAsync(mine, CancellationToken.None);

        await _store.TouchAsync(mine.Id, Now.AddHours(1), Now.AddDays(31), CancellationToken.None);
        var touched = await _store.FindActiveAsync(mine.TokenHash, Now, CancellationToken.None);
        Assert.Equal(Now.AddDays(31), touched!.ExpiresAt);

        Assert.False(await _store.RevokeAsync(mine.Id, userId: 2, Now, CancellationToken.None));
        Assert.True(await _store.RevokeAsync(mine.Id, userId: 1, Now, CancellationToken.None));
        Assert.False(await _store.RevokeAsync(mine.Id, userId: 1, Now, CancellationToken.None));
        Assert.Null(await _store.FindActiveAsync(mine.TokenHash, Now, CancellationToken.None));
    }

    [Fact]
    public async Task Liste_les_sessions_actives_de_l_utilisateur_et_purge_les_perimees()
    {
        var active = NewSession(1);
        var expired = NewSession(1, Now.AddDays(-40));
        var other = NewSession(2);
        foreach (var s in new[] { active, expired, other })
        {
            await _store.AddAsync(s, CancellationToken.None);
        }

        var list = await _store.ListActiveAsync(1, Now, CancellationToken.None);
        Assert.Equal(active.Id, Assert.Single(list).Id);

        Assert.Equal(1, await _store.PurgeAsync(Now.AddDays(-30), CancellationToken.None));
        Assert.Equal(2, await _db.Sessions.CountAsync());
    }
}
