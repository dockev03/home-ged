using HomeGed.Api.Data;

namespace HomeGed.Api.Shares;

/// <summary>Stockage des liens de partage (base homeged).</summary>
public interface IShareLinkStore
{
    Task AddAsync(ShareLink link, CancellationToken cancellationToken);

    /// <summary>Liens non révoqués et non expirés de l'utilisateur, les plus récents d'abord.</summary>
    Task<IReadOnlyList<ShareLink>> ListActiveAsync(int ownerId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Révoque un lien de l'utilisateur ; false s'il n'existe pas, ne lui appartient pas ou est déjà révoqué.</summary>
    Task<bool> RevokeAsync(string id, int ownerId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Lien par son identifiant public, quel que soit son état ; null s'il n'existe pas.</summary>
    Task<ShareLink?> FindAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Compte un téléchargement, de façon atomique, si le lien est encore utilisable (non révoqué,
    /// non expiré, quota non atteint) ; false sinon.
    /// </summary>
    Task<bool> TryCountDownloadAsync(string id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ajoute une entrée au journal d'accès d'un lien (SHARE-05).</summary>
    Task LogAccessAsync(ShareAccessLog entry, CancellationToken cancellationToken);

    /// <summary>Journal d'un lien de l'utilisateur, le plus récent d'abord ; null si le lien ne lui appartient pas.</summary>
    Task<IReadOnlyList<ShareAccessLog>?> ListAccessLogAsync(string id, int ownerId, int limit, CancellationToken cancellationToken);

    /// <summary>Supprime les liens expirés ou révoqués avant la date donnée, avec leur journal d'accès.</summary>
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
