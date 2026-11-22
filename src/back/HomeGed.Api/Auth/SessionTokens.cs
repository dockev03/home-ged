using System.Buffers.Text;
using System.Security.Cryptography;

namespace HomeGed.Api.Auth;

/// <summary>Jeton opaque du cookie de session : 256 bits aléatoires ; la base n'en garde que le SHA-256.</summary>
public static class SessionTokens
{
    /// <summary>Préfixe __Host- : cookie Secure, sans Domain, sur « / » (imposé par le navigateur).</summary>
    public const string CookieName = "__Host-homeged-session";

    private const int TokenBytes = 32;

    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>Empreinte du jeton, ou null s'il n'a pas le bon format.</summary>
    public static byte[]? Hash(string? token)
    {
        // TryDecodeFromChars lève une exception sur un caractère invalide : on valide d'abord.
        if (string.IsNullOrEmpty(token) || token.Length > 64
            || !Base64Url.IsValid(token, out var length) || length != TokenBytes)
        {
            return null;
        }

        Span<byte> raw = stackalloc byte[TokenBytes];
        return Base64Url.TryDecodeFromChars(token, raw, out _) ? SHA256.HashData(raw) : null;
    }
}
