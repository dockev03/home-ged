using System.Collections.Concurrent;
using HomeGed.Api.Data;
using HomeGed.Api.Shares;

namespace HomeGed.Api.Tests;

/// <summary>Stockage des liens en mémoire (le stockage EF est couvert par les tests d'intégration).</summary>
internal sealed class InMemoryShareLinkStore : IShareLinkStore
{
    public ConcurrentDictionary<string, ShareLink> Links { get; } = new(StringComparer.Ordinal);

    public Task AddAsync(ShareLink link, CancellationToken cancellationToken)
    {
        Links[link.Id] = Copy(link);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ShareLink>> ListActiveAsync(int ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ShareLink>>(Links.Values
            .Where(l => l.OwnerId == ownerId && l.RevokedAt is null && l.ExpiresAt > now)
            .OrderByDescending(l => l.CreatedAt)
            .Select(Copy)
            .ToList());

    public Task<bool> RevokeAsync(string id, int ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Links.TryGetValue(id, out var l) && l.OwnerId == ownerId && l.RevokedAt is null)
        {
            l.RevokedAt = now;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public ConcurrentQueue<ShareAccessLog> AccessLog { get; } = new();

    public Task<ShareLink?> FindAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(Links.TryGetValue(id, out var l) ? Copy(l) : null);

    public Task<bool> TryCountDownloadAsync(string id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Links.TryGetValue(id, out var l))
        {
            return Task.FromResult(false);
        }

        lock (l)
        {
            if (l.RevokedAt is not null || l.ExpiresAt <= now || (l.MaxDownloads is { } max && l.DownloadCount >= max))
            {
                return Task.FromResult(false);
            }

            l.DownloadCount++;
            return Task.FromResult(true);
        }
    }

    public Task LogAccessAsync(ShareAccessLog entry, CancellationToken cancellationToken)
    {
        AccessLog.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ShareAccessLog>?> ListAccessLogAsync(string id, int ownerId, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ShareAccessLog>?>(
            Links.TryGetValue(id, out var l) && l.OwnerId == ownerId
                ? AccessLog.Where(a => a.ShareLinkId == id).OrderByDescending(a => a.Timestamp).Take(limit).ToList()
                : null);

    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken) => Task.FromResult(0);

    private static ShareLink Copy(ShareLink l) => new()
    {
        Id = l.Id,
        DocumentId = l.DocumentId,
        OwnerId = l.OwnerId,
        PasswordHash = l.PasswordHash,
        CreatedAt = l.CreatedAt,
        ExpiresAt = l.ExpiresAt,
        MaxDownloads = l.MaxDownloads,
        DownloadCount = l.DownloadCount,
        RevokedAt = l.RevokedAt,
    };
}
