using System.Buffers.Text;
using System.Security.Cryptography;

namespace HomeGed.Api.Shares;

/// <summary>Identifiant public d'un lien : 128 bits aléatoires (CSPRNG), encodés en base64url.</summary>
public static class ShareLinkIds
{
    private const int Bytes = 16;

    /// <summary>Longueur de l'identifiant encodé (base64url sans remplissage).</summary>
    public const int Length = 22;

    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes));

    /// <summary>Vrai si la chaîne a la forme d'un identifiant de lien.</summary>
    public static bool IsValid(string? id) =>
        id is { Length: Length } && Base64Url.IsValid(id, out var decoded) && decoded == Bytes;
}
