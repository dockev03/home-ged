using HomeGed.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeGed.Api.Shares;

public sealed class EfShareLinkStore(HomeGedDbContext db) : IShareLinkStore
{
    public async Task AddAsync(ShareLink link, CancellationToken cancellationToken)
    {
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShareLink>> ListActiveAsync(int ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.ShareLinks.AsNoTracking()
            .Where(l => l.OwnerId == ownerId && l.RevokedAt == null && l.ExpiresAt > now)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> RevokeAsync(string id, int ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.ShareLinks.Where(l => l.Id == id && l.OwnerId == ownerId && l.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.RevokedAt, now), cancellationToken) > 0;

    public Task<ShareLink?> FindAsync(string id, CancellationToken cancellationToken) =>
        db.ShareLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    /// <summary>Une seule requête UPDATE conditionnelle : deux téléchargements simultanés ne dépassent jamais le quota.</summary>
    public async Task<bool> TryCountDownloadAsync(string id, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.ShareLinks
            .Where(l => l.Id == id && l.RevokedAt == null && l.ExpiresAt > now
                && (l.MaxDownloads == null || l.DownloadCount < l.MaxDownloads))
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.DownloadCount, l => l.DownloadCount + 1), cancellationToken) > 0;

    public async Task LogAccessAsync(ShareAccessLog entry, CancellationToken cancellationToken)
    {
        db.ShareAccessLog.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShareAccessLog>?> ListAccessLogAsync(
        string id, int ownerId, int limit, CancellationToken cancellationToken)
    {
        if (!await db.ShareLinks.AnyAsync(l => l.Id == id && l.OwnerId == ownerId, cancellationToken))
        {
            return null;
        }

        return await db.ShareAccessLog.AsNoTracking()
            .Where(a => a.ShareLinkId == id)
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Le journal d'accès est supprimé en cascade par la base.</summary>
    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        db.ShareLinks.Where(l => l.ExpiresAt < before || l.RevokedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
}
