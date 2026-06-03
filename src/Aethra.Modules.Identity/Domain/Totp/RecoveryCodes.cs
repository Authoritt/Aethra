using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Aethra.Modules.Identity.Domain.Totp;

/// <summary>
/// F12.1B — codigos de recuperacion one-shot para usuarios con TOTP. Cada code es 8 chars
/// alfanumericos (ej. <c>A1B2C3D4</c>). Se generan 10 al activar 2FA. Una vez usado, queda
/// marcado en el bitmask y no puede reutilizarse.
/// </summary>
public static class RecoveryCodes
{
    public const int CodeCount = 10;
    public const int CodeLength = 8;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sin I/O/0/1 para evitar confusion

    public static IReadOnlyList<string> Generate(int count = CodeCount)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            codes.Add(NewCode());
        }
        return codes;
    }

    public static string NewCode()
    {
        var sb = new StringBuilder(CodeLength);
        Span<byte> buf = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(buf);
        foreach (var b in buf)
        {
            sb.Append(Alphabet[b % Alphabet.Length]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Serializa los codigos como newline-separated UTF-8. El cifrado de la serializacion
    /// completa lo hace el codec DataProtection.
    /// </summary>
    public static byte[] Pack(IReadOnlyList<string> codes)
        => Encoding.UTF8.GetBytes(string.Join('\n', codes));

    /// <summary>
    /// Devuelve los 10 codigos en el mismo orden persistido para que el handler de verify
    /// pueda asociar el bit index del bitmask con cada codigo.
    /// </summary>
    public static IReadOnlyList<string> Unpack(byte[] packed)
    {
        if (packed is null || packed.Length == 0) { return []; }
        var raw = Encoding.UTF8.GetString(packed);
        return raw.Split('\n', StringSplitOptions.None);
    }

    /// <summary>
    /// Valida que <paramref name="input"/> tiene formato de recovery code (8 chars alfanum
    /// del alfabeto reducido). No verifica contra el storage; solo discrimina entre formato
    /// TOTP (6 digitos) y recovery (8 chars).
    /// </summary>
    public static bool LooksLikeRecoveryCode(string? input)
    {
        if (string.IsNullOrEmpty(input) || input.Length != CodeLength) { return false; }
        foreach (var c in input.ToUpperInvariant())
        {
            if (Alphabet.IndexOf(c, StringComparison.Ordinal) < 0) { return false; }
        }
        return true;
    }

    /// <summary>Normaliza recovery code a uppercase invariant.</summary>
    public static string Normalize(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        return input.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Marca el bit <paramref name="index"/> como usado. Devuelve true si el cambio efectivo.
    /// </summary>
    public static bool TrySetUsed(ref int mask, int index)
    {
        if (index < 0 || index >= CodeCount) { return false; }
        var bit = 1 << index;
        if ((mask & bit) != 0) { return false; }
        mask |= bit;
        return true;
    }

    public static bool IsUsed(int mask, int index)
    {
        if (index < 0 || index >= CodeCount) { return true; }
        return (mask & (1 << index)) != 0;
    }

    public static int CountUsed(int mask)
    {
        var count = 0;
        for (var i = 0; i < CodeCount; i++)
        {
            if (IsUsed(mask, i)) { count++; }
        }
        return count;
    }

    public static int RemainingCount(int mask) => CodeCount - CountUsed(mask);

    public static string FormatRemainingFraction(int mask)
        => string.Create(CultureInfo.InvariantCulture, $"{RemainingCount(mask)}/{CodeCount}");
}
