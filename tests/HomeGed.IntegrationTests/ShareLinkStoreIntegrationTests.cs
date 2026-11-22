using HomeGed.Api.Data;
using HomeGed.Api.Shares;
using Microsoft.EntityFrameworkCore;

namespace HomeGed.IntegrationTests;

/// <summary>Base homeged : stockage des liens de partage sur un PostgreSQL réel.</summary>
[Collection(BaseHomeGed.Name)]
public sealed class ShareLinkStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    private HomeGedDbContext _db = null!;

    private EfShareLinkStore _store = null!;

    public async Task InitializeAsync()
    {
        _db = new HomeGedDbContext(new DbContextOptionsBuilder<HomeGedDbContext>()
            .UseNpgsql(IntegrationEnvironment.HomeGedDb).Options);
        await _db.Database.MigrateAsync();
        await _db.ShareLinks.ExecuteDeleteAsync();
        _store = new EfShareLinkStore(_db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _db.Dispose();

    private static ShareLink NewLink(int ownerId, DateTimeOffset? createdAt = null, DateTimeOffset? expiresAt = null) => new()
    {
        Id = ShareLinkIds.New(),
        DocumentId = 40,
        OwnerId = ownerId,
        PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c2Vs$ZW1wcmVpbnRl",
        CreatedAt = createdAt ?? Now,
        ExpiresAt = expiresAt ?? Now.AddDays(7),
        MaxDownloads = 3,
    };

    [Fact]
    public async Task Liste_les_liens_actifs_du_proprietaire_les_plus_recents_d_abord()
    {
        var older = NewLink(1, Now.AddHours(-2));
        var newer = NewLink(1, Now.AddHours(-1));
        var expired = NewLink(1, Now.AddDays(-8), Now);
        var other = NewLink(2);
        foreach (var link in new[] { older, newer, expired, other })
        {
            await _store.AddAsync(link, CancellationToken.None);
        }

        var links = await _store.ListActiveAsync(1, Now, CancellationToken.None);

        Assert.Equal([newer.Id, older.Id], links.Select(l => l.Id));
        Assert.Equal(3, links[0].MaxDownloads);
        Assert.Equal(0, links[0].DownloadCount);
    }

    [Fact]
    public async Task Revoque_seulement_ses_liens_une_seule_fois()
    {
        var link = NewLink(1);
        await _store.AddAsync(link, CancellationToken.None);

        Assert.False(await _store.RevokeAsync(link.Id, ownerId: 2, Now, CancellationToken.None));
        Assert.True(await _store.RevokeAsync(link.Id, ownerId: 1, Now, CancellationToken.None));
        Assert.False(await _store.RevokeAsync(link.Id, ownerId: 1, Now, CancellationToken.None));
        Assert.Empty(await _store.ListActiveAsync(1, Now, CancellationToken.None));
    }

    [Fact]
    public async Task Purge_les_liens_perimes_et_leur_journal_en_cascade()
    {
        var expired = NewLink(1, Now.AddDays(-60), Now.AddDays(-40));
        var revoked = NewLink(1, Now.AddDays(-60), Now.AddDays(10));
        var recent = NewLink(1, Now.AddDays(-5), Now.AddDays(-1));
        var active = NewLink(1);
        foreach (var link in new[] { expired, revoked, recent, active })
        {
            await _store.AddAsync(link, CancellationToken.None);
        }

        await _store.RevokeAsync(revoked.Id, 1, Now.AddDays(-35), CancellationToken.None);
        _db.ShareAccessLog.Add(new ShareAccessLog { ShareLinkId = expired.Id, Timestamp = Now.AddDays(-50), Ip = "203.0.113.1", Success = true });
        _db.ShareAccessLog.Add(new ShareAccessLog { ShareLinkId = active.Id, Timestamp = Now, Ip = "203.0.113.1", Success = false });
        await _db.SaveChangesAsync();

        var deleted = await _store.PurgeAsync(Now.AddDays(-30), CancellationToken.None);

        Assert.Equal(2, deleted);
        var left = await _db.ShareLinks.AsNoTracking().Select(l => l.Id).ToListAsync();
        Assert.Equal(new[] { recent.Id, active.Id }.Order(StringComparer.Ordinal), left.Order(StringComparer.Ordinal));
        Assert.Equal([active.Id], await _db.ShareAccessLog.AsNoTracking().Select(a => a.ShareLinkId).ToListAsync());
    }
    [Fact]
    public async Task Retrouve_un_lien_quel_que_soit_son_etat()
    {
        var link = NewLink(1);
        await _store.AddAsync(link, CancellationToken.None);
        await _store.RevokeAsync(link.Id, 1, Now, CancellationToken.None);

        var found = await _store.FindAsync(link.Id, CancellationToken.None);

        Assert.NotNull(found?.RevokedAt);
        Assert.Null(await _store.FindAsync(ShareLinkIds.New(), CancellationToken.None));
    }

    [Fact]
    public async Task Compte_les_telechargements_sans_depasser_le_quota_meme_en_parallele()
    {
        var link = NewLink(1);
        await _store.AddAsync(link, CancellationToken.None);

        // Un contexte par requête, comme en production : des UPDATE réellement concurrents.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new HomeGedDbContext(new DbContextOptionsBuilder<HomeGedDbContext>()
                .UseNpgsql(IntegrationEnvironment.HomeGedDb).Options);
            return await new EfShareLinkStore(db).TryCountDownloadAsync(link.Id, Now, CancellationToken.None);
        }));

        Assert.Equal(3, results.Count(r => r));
        Assert.Equal(3, (await _store.FindAsync(link.Id, CancellationToken.None))!.DownloadCount);
    }

    [Fact]
    public async Task Refuse_de_compter_un_telechargement_sur_un_lien_expire_ou_revoque()
    {
        var expired = NewLink(1, Now.AddDays(-8), Now);
        var revoked = NewLink(1);
        await _store.AddAsync(expired, CancellationToken.None);
        await _store.AddAsync(revoked, CancellationToken.None);
        await _store.RevokeAsync(revoked.Id, 1, Now, CancellationToken.None);

        Assert.False(await _store.TryCountDownloadAsync(expired.Id, Now, CancellationToken.None));
        Assert.False(await _store.TryCountDownloadAsync(revoked.Id, Now, CancellationToken.None));
    }

    [Fact]
    public async Task Journal_reserve_au_proprietaire_le_plus_recent_d_abord()
    {
        var link = NewLink(1);
        await _store.AddAsync(link, CancellationToken.None);
        await _store.LogAccessAsync(
            new ShareAccessLog { ShareLinkId = link.Id, Timestamp = Now.AddMinutes(-1), Ip = "203.0.113.1", Success = false },
            CancellationToken.None);
        await _store.LogAccessAsync(
            new ShareAccessLog { ShareLinkId = link.Id, Timestamp = Now, Ip = "203.0.113.2", UserAgent = "Firefox", Success = true },
            CancellationToken.None);

        var entries = await _store.ListAccessLogAsync(link.Id, 1, 10, CancellationToken.None);

        Assert.Equal(["203.0.113.2", "203.0.113.1"], entries!.Select(e => e.Ip));
        Assert.Single((await _store.ListAccessLogAsync(link.Id, 1, 1, CancellationToken.None))!);
        Assert.Null(await _store.ListAccessLogAsync(link.Id, 2, 10, CancellationToken.None));
    }
}
