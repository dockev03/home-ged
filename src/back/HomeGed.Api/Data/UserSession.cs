namespace HomeGed.Api.Data;

/// <summary>
/// Session Home-GED d'un utilisateur Paperless (table <c>sessions</c>).
/// Le navigateur ne détient que le jeton du cookie ; la base n'en garde que l'empreinte.
/// </summary>
public sealed class UserSession
{
    /// <summary>Identifiant public (liste et révocation des sessions).</summary>
    public Guid Id { get; set; }

    /// <summary>SHA-256 du jeton du cookie : une fuite de la base ne permet pas de se connecter.</summary>
    public byte[] TokenHash { get; set; } = [];

    /// <summary>Identifiant de l'utilisateur dans Paperless.</summary>
    public int UserId { get; set; }

    public string Username { get; set; } = "";

    /// <summary>Token API Paperless, chiffré par Data Protection.</summary>
    public string PaperlessToken { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Fin de validité : glisse à chaque usage, sans dépasser la durée maximale.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }
}
