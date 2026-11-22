using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace HomeGed.Api.Shares;

/// <summary>
/// Hachage Argon2id des mots de passe des liens, aux paramètres recommandés par l'OWASP
/// (19 Mio, 2 passes, 1 thread). Format PHC :
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;sel&gt;$&lt;empreinte&gt;</c> (base64 sans remplissage).
/// </summary>
public static class SharePasswordHasher
{
    public const int MinLength = 8;

    public const int MaxLength = 128;

    internal const int MemoryKib = 19 * 1024;

    internal const int Iterations = 2;

    internal const int Parallelism = 1;

    private const int SaltBytes = 16;

    private const int HashBytes = 32;

    private const string Prefix = "$argon2id$v=19$";

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, MemoryKib, Iterations, Parallelism, HashBytes);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}m={MemoryKib},t={Iterations},p={Parallelism}${Encode(salt)}${Encode(hash)}");
    }

    /// <summary>Compare en temps constant ; faux si l'empreinte stockée est illisible.</summary>
    public static bool Verify(string password, string encoded)
    {
        ArgumentNullException.ThrowIfNull(password);
        // Un lien n'est jamais créé sans mot de passe : la saisie vide est toujours fausse.
        if (password.Length == 0 || !TryParse(encoded, out var memory, out var iterations, out var parallelism, out var salt, out var expected))
        {
            return false;
        }

        var actual = Derive(password, salt, memory, iterations, parallelism, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int memory, int iterations, int parallelism, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }

    /// <summary>Lit une empreinte PHC ; les paramètres sont bornés pour ne jamais coûter plus que prévu.</summary>
    private static bool TryParse(
        string encoded, out int memory, out int iterations, out int parallelism, out byte[] salt, out byte[] hash)
    {
        memory = iterations = parallelism = 0;
        salt = hash = [];
        if (string.IsNullOrEmpty(encoded) || !encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = encoded[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        foreach (var parameter in parts[0].Split(','))
        {
            var pair = parameter.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            switch (pair[0])
            {
                case "m": memory = value; break;
                case "t": iterations = value; break;
                case "p": parallelism = value; break;
                default: return false;
            }
        }

        if (memory is < 8 or > 256 * 1024 || iterations is < 1 or > 10 || parallelism is < 1 or > 8)
        {
            return false;
        }

        try
        {
            salt = Decode(parts[1]);
            hash = Decode(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= 8 && hash.Length is >= 16 and <= 64;
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[] Decode(string text) =>
        Convert.FromBase64String(text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '='));
}
