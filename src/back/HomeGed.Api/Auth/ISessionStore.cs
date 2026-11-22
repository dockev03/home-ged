using HomeGed.Api.Data;

namespace HomeGed.Api.Auth;

/// <summary>Stockage des sessions (base homeged).</summary>
public interface ISessionStore
{
    Task AddAsync(UserSession session, CancellationToken cancellationToken);

    /// <summary>Session non révoquée et non expirée correspondant à l'empreinte, sinon null.</summary>
    Task<UserSession?> FindActiveAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Enregistre un usage : dernière activité et nouvelle échéance.</summary>
    Task TouchAsync(Guid id, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSession>> ListActiveAsync(int userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Révoque une session de l'utilisateur ; false si elle n'existe pas ou ne lui appartient pas.</summary>
    Task<bool> RevokeAsync(Guid id, int userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Supprime les sessions expirées ou révoquées avant la date donnée.</summary>
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
