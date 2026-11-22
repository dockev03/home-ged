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

    /// <summary>Supprime les liens expirés ou révoqués avant la date donnée, avec leur journal d'accès.</summary>
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
