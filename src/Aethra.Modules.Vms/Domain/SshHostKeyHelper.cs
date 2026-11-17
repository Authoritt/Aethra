using System.Security.Cryptography;

namespace Aethra.Modules.Vms.Domain;

/// <summary>
/// Utilidades para formatear, normalizar y validar huellas SHA256 de claves de host SSH.
/// Formato canónico: <c>SHA256:&lt;base64-sin-padding&gt;</c> (estándar OpenSSH).
/// </summary>
public static class SshHostKeyHelper
{
    public const string Sha256Prefix = "SHA256:";

    /// <summary>
    /// Calcula la huella SHA256 canónica de una clave pública de host SSH a partir de sus bytes crudos.
    /// Devuelve el formato OpenSSH estándar: <c>SHA256:&lt;base64-sin-padding&gt;</c>.
    /// </summary>
    public static string FormatFingerprint(byte[] hostKey)
    {
        ArgumentNullException.ThrowIfNull(hostKey);
        var hash = SHA256.HashData(hostKey);
        return $"{Sha256Prefix}{Convert.ToBase64String(hash).TrimEnd('=')}";
    }

    /// <summary>
    /// Normaliza una huella para comparación y almacenamiento consistente.
    /// Acepta formatos con o sin prefijo <c>SHA256:</c> / <c>sha256:</c>, y con o sin padding <c>=</c>.
    /// Devuelve <c>SHA256:&lt;base64-sin-padding&gt;</c> o <c>null</c> si la entrada es nula o vacía.
    /// </summary>
    public static string? Normalize(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var trimmed = fingerprint.Trim();
        if (trimmed.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[Sha256Prefix.Length..].Trim();
        }

        trimmed = trimmed.TrimEnd('=');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return $"{Sha256Prefix}{trimmed}";
    }

    /// <summary>
    /// Compara dos huellas de forma segura e independiente del formato de entrada.
    /// </summary>
    public static bool FingerprintsMatch(string? expected, string? actual)
    {
        var normExpected = Normalize(expected);
        var normActual = Normalize(actual);

        if (normExpected is null || normActual is null)
        {
            return false;
        }

        return string.Equals(normExpected, normActual, StringComparison.Ordinal);
    }

    /// <summary>
    /// Valida si una huella corresponde a un hash SHA-256 válido (32 bytes = 256 bits).
    /// </summary>
    public static bool IsValidFingerprint(string? fingerprint)
    {
        var norm = Normalize(fingerprint);
        if (norm is null)
        {
            return false;
        }

        var raw = norm[Sha256Prefix.Length..];
        var mod = raw.Length % 4;
        var padded = mod == 0 ? raw : raw + new string('=', 4 - mod);

        Span<byte> buffer = stackalloc byte[64];
        return Convert.TryFromBase64String(padded, buffer, out var bytesWritten) && bytesWritten == 32;
    }

    /// <summary>
    /// Formato canónico legible para logs y mensajes de error.
    /// </summary>
    public static string CanonicalDisplay(string? fingerprint)
        => Normalize(fingerprint) ?? "(ninguna)";
}
