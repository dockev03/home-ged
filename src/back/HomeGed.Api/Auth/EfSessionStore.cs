using HomeGed.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeGed.Api.Auth;

public sealed class EfSessionStore(HomeGedDbContext db) : ISessionStore
{
    public async Task AddAsync(UserSession session, CancellationToken cancellationToken)
    {
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<UserSession?> FindActiveAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);

    public Task TouchAsync(Guid id, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        db.Sessions.Where(s => s.Id == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.LastSeenAt, lastSeenAt)
                .SetProperty(s => s.ExpiresAt, expiresAt), cancellationToken);

    public async Task<IReadOnlyList<UserSession>> ListActiveAsync(int userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> RevokeAsync(Guid id, int userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Sessions.Where(s => s.Id == id && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), cancellationToken) > 0;

    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        db.Sessions.Where(s => s.ExpiresAt < before || s.RevokedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
}
