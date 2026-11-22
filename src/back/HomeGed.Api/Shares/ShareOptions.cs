using System.ComponentModel.DataAnnotations;

namespace HomeGed.Api.Shares;

/// <summary>Liens de partage protégés (section « Share »).</summary>
public sealed class ShareOptions
{
    public const string Section = "Share";

    /// <summary>Durée de validité maximale d'un lien, en jours (SHARE-01).</summary>
    [Range(1, 365)]
    public int MaxExpirationDays { get; set; } = 30;
}
