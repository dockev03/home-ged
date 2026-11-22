using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace HomeGed.Api.Shares;

/// <summary>
/// Ticket d'accès à un lien, délivré après le bon mot de passe (SHARE-03) : cookie <c>HttpOnly</c>,
/// <c>Secure</c>, <c>SameSite=Strict</c>, limité au chemin du lien, chiffré et signé par Data Protection.
/// Il évite de redemander le mot de passe pour l'aperçu et le téléchargement, et n'apparaît jamais dans une URL.
/// </summary>
public sealed class ShareTickets(IDataProtectionProvider provider, TimeProvider time)
{
    /// <summary>Préfixe __Secure- : le navigateur n'accepte le cookie qu'en HTTPS.</summary>
    public const string CookieName = "__Secure-homeged-partage";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IDataProtector _protector = provider.CreateProtector("HomeGed.Shares.Ticket.v1");

    /// <summary>Chemin des routes publiques d'un lien : le cookie n'est envoyé qu'à elles.</summary>
    public static string PathFor(string linkId) => $"/api/public/partages/{linkId}";

    public void Issue(HttpContext context, string linkId, DateTimeOffset linkExpiresAt)
    {
        var now = time.GetUtcNow();
        var expires = now + Lifetime < linkExpiresAt ? now + Lifetime : linkExpiresAt;
        var payload = string.Create(CultureInfo.InvariantCulture, $"{linkId}|{expires.ToUnixTimeSeconds()}");
        context.Response.Cookies.Append(CookieName, _protector.Protect(payload), Options(linkId, expires));
    }

    /// <summary>Vrai si la requête porte un ticket valide, non expiré, émis pour ce lien.</summary>
    public bool IsValid(HttpContext context, string linkId)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var parts = payload.Split('|');
        return parts.Length == 2
            && string.Equals(parts[0], linkId, StringComparison.Ordinal)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && DateTimeOffset.FromUnixTimeSeconds(seconds) > time.GetUtcNow();
    }

    public static void Delete(HttpContext context, string linkId) =>
        context.Response.Cookies.Delete(CookieName, Options(linkId, null));

    private static CookieOptions Options(string linkId, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = PathFor(linkId),
        Expires = expires,
        IsEssential = true,
    };
}
