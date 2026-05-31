using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Servicio de hash determinístico para API keys. Razón del diseño:
///
/// El handler de autenticación recibe el secret presentado por el cliente y necesita
/// localizar el registro asociado en BD. Un hash con salt aleatorio (bcrypt, Argon2id
/// estándar) obligaría a un scan completo de la tabla — inaceptable para un hot path.
/// Por eso usamos Argon2id con un salt determinístico (derivado de un pepper fijo de
/// la versión del esquema), lo que produce un mismo hash para un mismo plaintext y
/// permite lookup por índice en O(log n).
///
/// La resistencia a fuerza bruta offline depende entonces de la entropía del secret,
/// no del costo del hash: los secrets generados por <see cref="ApiKeyGenerator"/> tienen
/// 160 bits de entropía (32 caracteres Base32 sobre 20 bytes random), suficiente para
/// que el coste de invertir un solo hash sea prohibitivo incluso si Argon2 estuviera
/// configurado con parámetros relajados.
///
/// Mismo patrón que <c>Aethra.Modules.Vms.Domain.SatelliteToken.HashOnly</c>.
/// </summary>
public interface IApiKeyHasher
{
    /// <summary>Devuelve el hash determinístico (32 bytes) del secret presentado.</summary>
    byte[] Hash(string plain);

    /// <summary>Comparación constant-time entre el hash de <paramref name="plain"/> y el esperado.</summary>
    bool Verify(string plain, byte[] expectedHash);
}

/// <inheritdoc cref="IApiKeyHasher"/>
public sealed class ApiKeyHasher : IApiKeyHasher
{
    // Salt determinístico — versionar este string si en algún momento se rota el esquema
    // de hashing (ej. "aethra-api-key-v2"). Cambia el resultado para todos los secrets,
    // forzando re-emisión de keys.
    private static readonly byte[] FixedSalt = Encoding.UTF8.GetBytes("aethra-api-key-v1");

    public byte[] Hash(string plain)
    {
        ArgumentException.ThrowIfNullOrEmpty(plain);
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(plain))
        {
            Salt = FixedSalt,
            DegreeOfParallelism = 2,
            MemorySize = 32768,
            Iterations = 2,
        };
        return argon.GetBytes(32);
    }

    public bool Verify(string plain, byte[] expectedHash)
    {
        if (string.IsNullOrEmpty(plain) || expectedHash is null || expectedHash.Length == 0)
        {
            return false;
        }
        var actual = Hash(plain);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }
}
