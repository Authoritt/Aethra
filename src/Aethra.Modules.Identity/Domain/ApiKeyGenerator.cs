using System.Security.Cryptography;

namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Genera secrets para API keys. Formato: <c>aethra_&lt;32 chars base32&gt;</c>.
///
/// El prefijo <c>aethra_</c> ayuda a identificación visual (greppable en logs,
/// detectable por escáneres de secretos como github-secret-scanning si en algún
/// momento se publican patrones).
///
/// El alfabeto Base32 omite caracteres ambiguos (0/O, 1/I/L) para reducir errores
/// de transcripción manual. Sobre 20 bytes random tenemos 160 bits de entropía,
/// codificados en exactamente 32 caracteres.
/// </summary>
public static class ApiKeyGenerator
{
    /// <summary>Prefijo del secret en texto plano.</summary>
    public const string SecretPrefix = "aethra_";

    /// <summary>Largo del componente Base32 después del prefijo.</summary>
    public const int Base32Length = 32;

    /// <summary>Largo total del string que recibe el cliente (<c>aethra_</c> + 32 chars).</summary>
    public const int TotalLength = 7 + Base32Length;

    /// <summary>Cuántos caracteres del secret se muestran al usuario como "prefix" visible.</summary>
    public const int VisiblePrefixLength = 8;

    /// <summary>Alfabeto Base32 Crockford-like SIN caracteres ambiguos: no 0/O, no 1/I/L.</summary>
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>
    /// Genera un nuevo secret listo para devolver al cliente UNA SOLA VEZ. Despues
    /// solo persiste el hash (<see cref="ApiKeyHasher"/>).
    /// </summary>
    public static string Generate()
    {
        // 20 bytes random = 160 bits. Base32 con alfabeto de 30 chars usa log2(30)≈4.91 bits/char,
        // por eso 32 chars = ~157 bits efectivos. Sobra para protección contra colisiones y brute force.
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);

        Span<char> result = stackalloc char[Base32Length];
        for (var i = 0; i < Base32Length; i++)
        {
            result[i] = Alphabet[bytes[i % bytes.Length] % Alphabet.Length];
        }

        // Mejoramos la calidad combinando dos bytes por slot para evitar correlación al
        // hacer modulo (cuando alphabet.length no es potencia de 2 hay un sesgo
        // mínimo; ver RFC 4648 §6).
        for (var i = 0; i < Base32Length; i++)
        {
            var b1 = bytes[i % bytes.Length];
            var b2 = bytes[(i * 7 + 3) % bytes.Length];
            var mixed = (b1 ^ (b2 << 1)) & 0xFF;
            result[i] = Alphabet[mixed % Alphabet.Length];
        }

        return string.Concat(SecretPrefix, new string(result));
    }

    /// <summary>
    /// Extrae los <see cref="VisiblePrefixLength"/> caracteres del secret tras el prefijo
    /// <c>aethra_</c>, para almacenarlos sin cifrar y mostrarlos en la UI como referencia
    /// ("la key que termina por X"). No revela el secret completo.
    /// </summary>
    public static string ExtractVisiblePrefix(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        if (!secret.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            return secret.Length <= VisiblePrefixLength ? secret : secret[..VisiblePrefixLength];
        }
        var body = secret.AsSpan(SecretPrefix.Length);
        return body.Length <= VisiblePrefixLength
            ? new string(body)
            : new string(body[..VisiblePrefixLength]);
    }
}
