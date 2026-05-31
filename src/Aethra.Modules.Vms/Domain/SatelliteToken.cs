using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Aethra.Modules.Vms.Domain;

/// <summary>
/// Token de autenticación de un satélite. Guardamos solo el hash Argon2id; el plaintext
/// se devuelve UNA SOLA VEZ al rotar para que el operador lo copie al script de instalación.
/// </summary>
public sealed class SatelliteToken
{
    public string Hash { get; private set; }
    public DateTimeOffset RotatedAt { get; private set; }

    private SatelliteToken(string hash, DateTimeOffset rotatedAt)
    {
        Hash = hash;
        RotatedAt = rotatedAt;
    }

    /// <summary>
    /// Genera un nuevo token. Devuelve tupla <c>(plaintext, instancia)</c> — el plaintext
    /// es mostrado al usuario una sola vez, luego solo queda el hash.
    /// </summary>
    public static (string Plaintext, SatelliteToken Token) Issue(DateTimeOffset now)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = "sat_" + Convert.ToHexStringLower(bytes);
        return (plaintext, new SatelliteToken(hash: HashToken(plaintext), rotatedAt: now));
    }

    public bool Verify(string presented)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }
        var presentedHash = HashToken(presented);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presentedHash),
            Encoding.UTF8.GetBytes(Hash));
    }

    /// <summary>
    /// Hash determinístico expuesto para que el autenticador pueda comparar O(1) por hash.
    /// Convención: salt fijo + Argon2id, ver doc en <see cref="HashToken"/>.
    /// </summary>
    public static string HashOnly(string plaintext) => HashToken(plaintext);

    private static string HashToken(string plaintext)
    {
        // Argon2id con salt fijo derivado del prefijo + length corto. Razón: los tokens son
        // de alta entropía (256 bits), no necesitan salt per-token. La verificación tiene que
        // ser determinística para poder hacer lookup en BD por hash.
        var salt = Encoding.UTF8.GetBytes("aethra-satellite-token-v1");
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(plaintext))
        {
            Salt = salt,
            DegreeOfParallelism = 2,
            MemorySize = 32768,
            Iterations = 2,
        };
        return Convert.ToHexStringLower(argon.GetBytes(32));
    }

    // EF Core
    private SatelliteToken() : this(string.Empty, default) { }
}
