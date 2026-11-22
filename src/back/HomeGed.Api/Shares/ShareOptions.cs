using System.ComponentModel.DataAnnotations;

namespace HomeGed.Api.Shares;

/// <summary>Liens de partage protégés (section « Share »).</summary>
public sealed class ShareOptions
{
    public const string Section = "Share";

    /// <summary>Durée de validité maximale d'un lien, en jours (SHARE-01).</summary>
    [Range(1, 365)]
    public int MaxExpirationDays { get; set; } = 30;

    /// <summary>
    /// Token API du compte de service Paperless en lecture seule, utilisé par la page publique des
    /// liens (§3.2). Secret : variable d'environnement <c>Share__ServiceToken</c>. Vide : les liens
    /// ne s'ouvrent pas (message générique), le reste de l'application fonctionne.
    /// </summary>
    public string ServiceToken { get; set; } = "";
}
