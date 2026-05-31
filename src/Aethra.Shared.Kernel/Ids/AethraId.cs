using System.Diagnostics.CodeAnalysis;

namespace Aethra.Shared.Kernel.Ids;

/// <summary>
/// Identificador estable con prefijo de tipo: <c>app_01HXXX...</c>, <c>vm_01HXXX...</c>.
/// Internamente Guid v7 (lexicográficamente ordenable por tiempo) codificado en Base32 Crockford.
///
/// Por qué prefijo: un agente IA (o un humano) puede inferir el tipo del recurso por el prefijo
/// sin tener que consultar el endpoint — mismo patrón que usa Stripe (<c>cus_</c>, <c>ch_</c>).
/// </summary>
public readonly record struct AethraId
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string Prefix { get; }
    public Guid Value { get; }

    public AethraId(string prefix, Guid value)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Prefix requerido.", nameof(prefix));
        }
        Prefix = prefix.ToLowerInvariant();
        Value = value;
    }

    public static AethraId NewId(string prefix) => new(prefix, Guid.CreateVersion7());

    public override string ToString() => $"{Prefix}_{Encode(Value)}";

    public static bool TryParse(string? input, [NotNullWhen(true)] out AethraId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        var sep = input.IndexOf('_');
        if (sep <= 0 || sep == input.Length - 1)
        {
            return false;
        }
        var prefix = input[..sep];
        var encoded = input[(sep + 1)..];
        if (!TryDecode(encoded, out var guid))
        {
            return false;
        }
        id = new AethraId(prefix, guid);
        return true;
    }

    private static string Encode(Guid g)
    {
        var bytes = g.ToByteArray();
        // Base32 Crockford sobre 16 bytes → 26 caracteres.
        Span<char> output = stackalloc char[26];
        var bitBuffer = 0;
        var bitCount = 0;
        var outputIndex = 0;
        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                output[outputIndex++] = CrockfordAlphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }
        if (bitCount > 0)
        {
            output[outputIndex++] = CrockfordAlphabet[(bitBuffer << (5 - bitCount)) & 0x1F];
        }
        return new string(output[..outputIndex]);
    }

    private static bool TryDecode(string encoded, out Guid result)
    {
        result = Guid.Empty;
        if (encoded.Length != 26)
        {
            return false;
        }
        Span<byte> bytes = stackalloc byte[16];
        var bitBuffer = 0;
        var bitCount = 0;
        var byteIndex = 0;
        foreach (var c in encoded.ToUpperInvariant())
        {
            var i = CrockfordAlphabet.IndexOf(c);
            if (i < 0)
            {
                return false;
            }
            bitBuffer = (bitBuffer << 5) | i;
            bitCount += 5;
            if (bitCount >= 8)
            {
                bitCount -= 8;
                if (byteIndex >= 16)
                {
                    return false;
                }
                bytes[byteIndex++] = (byte)((bitBuffer >> bitCount) & 0xFF);
            }
        }
        if (byteIndex != 16)
        {
            return false;
        }
        result = new Guid(bytes);
        return true;
    }
}
