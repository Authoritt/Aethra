using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;

/// <summary>
/// Cifra/descifra el token API de Cloudflare con DataProtection. El purpose
/// <c>aethra-cloudflare-token</c> aisla la clave para que un compromiso de otro purpose
/// no exponga estos tokens.
/// </summary>
public interface ICloudflareTokenCodec
{
    byte[] Encode(string apiToken);
    string Decode(byte[] cipher);
}

public sealed class DataProtectionCloudflareTokenCodec : ICloudflareTokenCodec
{
    private const string Purpose = "aethra-cloudflare-token";

    private readonly IDataProtector _protector;

    public DataProtectionCloudflareTokenCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(string apiToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        var bytes = Encoding.UTF8.GetBytes(apiToken);
        return _protector.Protect(bytes);
    }

    public string Decode(byte[] cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        if (cipher.Length == 0)
        {
            throw new ArgumentException("cipher vacio.", nameof(cipher));
        }
        var plain = _protector.Unprotect(cipher);
        return Encoding.UTF8.GetString(plain);
    }
}
