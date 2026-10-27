using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Aethra.Modules.Identity.Domain.Totp;

/// <summary>
/// F12.1B — codigos de recuperacion one-shot para usuarios con TOTP. Cada code es 8 chars
/// alfanumericos (ej. <c>A1B2C3D4</c>). Se generan 10 al activar 2FA. Una vez usado, queda
/// marcado en el bitmask y no puede reutilizarse.
/// GH #91: Almacenados como hashes one-way (Argon2id) en lugar de texto plano cifrado reversible.
/// </summary>
public static class RecoveryCodes
{
    public const int CodeCount = 10;
    public const int CodeLength = 8;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sin I/O/0/1 para evitar confusion

    public static readonly byte[] HashedHeader = "AETHRA-RCH-V1\n"u8.ToArray();

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
    /// Comprueba si el payload persistido corresponde al formato de hashes one-way (Argon2id).
    /// </summary>
    public static bool IsHashed(byte[]? payload)
    {
        if (payload is null || payload.Length < HashedHeader.Length)
        {
            return false;
        }
        return payload.AsSpan(0, HashedHeader.Length).SequenceEqual(HashedHeader);
    }

    /// <summary>
    /// Hashea una lista de recovery codes en texto plano usando Argon2id (<see cref="PasswordHasher"/>)
    /// y devuelve el payload empaquetado con el encabezado identificador.
    /// </summary>
    public static byte[] Hash(IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var hashes = new List<string>(codes.Count);
        foreach (var code in codes)
        {
            var normalized = Normalize(code);
            hashes.Add(PasswordHasher.Hash(normalized));
        }
        var body = "AETHRA-RCH-V1\n" + string.Join('\n', hashes);
        return Encoding.UTF8.GetBytes(body);
    }

    /// <summary>
    /// Extrae la lista de hashes almacenados en un payload con formato <see cref="IsHashed"/>.
    /// </summary>
    public static IReadOnlyList<string> UnpackHashes(byte[] payload)
    {
        if (!IsHashed(payload))
        {
            return [];
        }
        var raw = Encoding.UTF8.GetString(payload.AsSpan(HashedHeader.Length));
        return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Busca el índice del recovery code en la lista de hashes one-way, ignorando los bits ya usados.
    /// Devuelve el índice (0..9) o -1 si no coincide. Tolera espacios y mayúsculas/minúsculas.
    /// </summary>
    public static int FindMatchingCodeIndex(string? enteredCode, IReadOnlyList<string> hashes, int usedMask)
    {
        if (string.IsNullOrWhiteSpace(enteredCode))
        {
            return -1;
        }
        var trimmed = enteredCode.Trim();
        if (!LooksLikeRecoveryCode(trimmed))
        {
            return -1;
        }
        var normalized = Normalize(trimmed);
        for (var i = 0; i < hashes.Count; i++)
        {
            if (IsUsed(usedMask, i))
            {
                continue;
            }
            if (PasswordHasher.Verify(normalized, hashes[i]))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Busca el índice de un recovery code legacy (texto plano), ignorando los bits ya usados.
    /// </summary>
    public static int FindMatchingLegacyCodeIndex(string? enteredCode, IReadOnlyList<string> legacyPlaintextCodes, int usedMask)
    {
        if (string.IsNullOrWhiteSpace(enteredCode))
        {
            return -1;
        }
        var trimmed = enteredCode.Trim();
        if (!LooksLikeRecoveryCode(trimmed))
        {
            return -1;
        }
        var normalized = Normalize(trimmed);
        for (var i = 0; i < legacyPlaintextCodes.Count; i++)
        {
            if (IsUsed(usedMask, i))
            {
                continue;
            }
            if (string.Equals(legacyPlaintextCodes[i], normalized, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Serializa los codigos como newline-separated UTF-8.
    /// Obsoleto: se mantiene para compatibilidad con datos legacy y deserialización.
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
