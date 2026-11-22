namespace HomeGed.Api.Data;

/// <summary>Tentative d'accès à un lien de partage (table <c>share_access_log</c>, SHARE-05).</summary>
public sealed class ShareAccessLog
{
    public long Id { get; set; }

    public string ShareLinkId { get; set; } = "";

    public DateTimeOffset Timestamp { get; set; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Mot de passe correct et lien utilisable.</summary>
    public bool Success { get; set; }
}
