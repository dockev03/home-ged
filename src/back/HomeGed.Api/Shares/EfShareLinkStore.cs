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

    /// <summary>Le journal d'accès est supprimé en cascade par la base.</summary>
    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        db.ShareLinks.Where(l => l.ExpiresAt < before || l.RevokedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
}
