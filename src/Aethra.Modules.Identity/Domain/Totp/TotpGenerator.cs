using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Aethra.Modules.Identity.Domain.Totp;

/// <summary>
/// F12.1B — implementacion RFC 6238 TOTP (HMAC-SHA1, 6 digitos, 30s window).
/// Sirve para validar y generar codigos TOTP de Google Authenticator/Authy/1Password.
/// </summary>
public static class TotpGenerator
{
    private const int DigitsDefault = 6;
    private const int PeriodSecondsDefault = 30;

    /// <summary>
    /// Genera el codigo TOTP para <paramref name="utcNow"/> (default <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    public static string Generate(byte[] secret, DateTimeOffset? utcNow = null,
        int digits = DigitsDefault, int periodSeconds = PeriodSecondsDefault)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var time = (utcNow ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var counter = time / periodSeconds;
        return GenerateForCounter(secret, counter, digits);
    }

    /// <summary>
    /// Valida <paramref name="code"/> contra el secret en una ventana de +/- <paramref name="windowSteps"/>
    /// steps (default 1 = +/- 30s) para tolerar drift de reloj. Tiempo constante para resistir
    /// timing attacks (XOR de iguales largos).
    /// </summary>
    public static bool ValidateCode(byte[] secret, string code, DateTimeOffset? utcNow = null,
        int digits = DigitsDefault, int periodSeconds = PeriodSecondsDefault, int windowSteps = 1)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (string.IsNullOrWhiteSpace(code) || code.Length != digits) { return false; }

        var time = (utcNow ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var counter = time / periodSeconds;

        var ok = false;
        for (var w = -windowSteps; w <= windowSteps; w++)
        {
            var generated = GenerateForCounter(secret, counter + w, digits);
            // Comparacion en tiempo constante.
            ok |= CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(generated),
                Encoding.ASCII.GetBytes(code));
        }
        return ok;
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 TOTP requires HMAC-SHA1 by spec; mandated by Google Authenticator/Authy/1Password.")]
    private static string GenerateForCounter(byte[] secret, long counter, int digits)
    {
        // 8 bytes big-endian del counter.
        Span<byte> counterBytes = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counterBytes, hash);

        // Dynamic truncation: offset = bajo nibble del ultimo byte.
        var offset = hash[19] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);

        var modulus = (int)Math.Pow(10, digits);
        var otp = binaryCode % modulus;
        return otp.ToString(new string('0', digits), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Genera un secret aleatorio (20 bytes = 160 bits HMAC-SHA1 estandar). Base32 con padding
    /// para uso en Google Authenticator (otpauth://).
    /// </summary>
    public static byte[] GenerateSecret(int bytes = 20)
    {
        var buf = new byte[bytes];
        RandomNumberGenerator.Fill(buf);
        return buf;
    }

    /// <summary>
    /// Base32 RFC 4648 (sin padding) para representar el secret en QR/otpauth URIs.
    /// </summary>
    public static string ToBase32(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (bytes.Length == 0) { return string.Empty; }
        var sb = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var bitBuffer = 0;
        var bitCount = 0;
        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                sb.Append(alphabet[(bitBuffer >> bitCount) & 0x1F]);
            }
        }
        if (bitCount > 0)
        {
            sb.Append(alphabet[(bitBuffer << (5 - bitCount)) & 0x1F]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Construye un otpauth URI para QR: <c>otpauth://totp/{issuer}:{account}?secret=...&amp;issuer=...</c>.
    /// </summary>
    public static string BuildOtpAuthUri(string issuer, string account, byte[] secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        var secretB32 = ToBase32(secret);
        var encIssuer = Uri.EscapeDataString(issuer);
        var encAccount = Uri.EscapeDataString(account);
        return string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{encIssuer}:{encAccount}?secret={secretB32}&issuer={encIssuer}&algorithm=SHA1&digits=6&period=30");
    }
}
