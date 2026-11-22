namespace HomeGed.Api.Data;

/// <summary>
/// Lien de partage protégé d'un document (table <c>share_links</c>, F-SHARE).
/// Aucune donnée du document n'est stockée : seul son identifiant Paperless.
/// </summary>
public sealed class ShareLink
{
    /// <summary>Identifiant public du lien : 128 bits aléatoires encodés en base64url (22 caractères).</summary>
    public string Id { get; set; } = "";

    /// <summary>Identifiant du document dans Paperless.</summary>
    public int DocumentId { get; set; }

    /// <summary>Identifiant Paperless du propriétaire du lien (l'utilisateur qui l'a créé).</summary>
    public int OwnerId { get; set; }

    /// <summary>Empreinte Argon2id du mot de passe, au format PHC ; jamais le mot de passe lui-même.</summary>
    public string PasswordHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Nombre maximal de téléchargements ; null pour aucune limite.</summary>
    public int? MaxDownloads { get; set; }

    public int DownloadCount { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public List<ShareAccessLog> AccessLog { get; set; } = [];
}
