using System.Collections.Concurrent;
using HomeGed.Api.Auth;
using HomeGed.Api.Data;

namespace HomeGed.Api.Tests;

/// <summary>Stockage des sessions en mémoire (le stockage EF est couvert par les tests d'intégration).</summary>
internal sealed class InMemorySessionStore : ISessionStore
{
    public ConcurrentDictionary<Guid, UserSession> Sessions { get; } = new();

    public Task AddAsync(UserSession session, CancellationToken cancellationToken)
    {
        Sessions[session.Id] = Copy(session);
        return Task.CompletedTask;
    }

    public Task<UserSession?> FindActiveAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.Values
            .Where(s => s.TokenHash.AsSpan().SequenceEqual(tokenHash) && s.RevokedAt is null && s.ExpiresAt > now)
            .Select(Copy)
            .FirstOrDefault());

    public Task TouchAsync(Guid id, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (Sessions.TryGetValue(id, out var s))
        {
            s.LastSeenAt = lastSeenAt;
            s.ExpiresAt = expiresAt;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserSession>> ListActiveAsync(int userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserSession>>(Sessions.Values
            .Where(s => s.UserId == userId && s.RevokedAt is null && s.ExpiresAt > now)
            .Select(Copy)
            .ToList());

    public Task<bool> RevokeAsync(Guid id, int userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Sessions.TryGetValue(id, out var s) && s.UserId == userId && s.RevokedAt is null)
        {
            s.RevokedAt = now;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken) => Task.FromResult(0);

    private static UserSession Copy(UserSession s) => new()
    {
        Id = s.Id,
        TokenHash = s.TokenHash,
        UserId = s.UserId,
        Username = s.Username,
        PaperlessToken = s.PaperlessToken,
        CreatedAt = s.CreatedAt,
        LastSeenAt = s.LastSeenAt,
        ExpiresAt = s.ExpiresAt,
        RevokedAt = s.RevokedAt,
        Ip = s.Ip,
        UserAgent = s.UserAgent,
    };
}
