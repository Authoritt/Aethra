using System.Text;
using Aethra.Modules.Settings.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Settings.Infrastructure.Credentials;

/// <summary>
/// Implementación de <see cref="IIntegrationCredentialCodec"/> basada en ASP.NET DataProtection.
/// El purpose <c>aethra-integration-creds</c> aisla la clave: comprometer otro purpose no
/// expone estos secretos.
/// </summary>
public sealed class DataProtectionIntegrationCredentialCodec : IIntegrationCredentialCodec
{
    private const string Purpose = "aethra-integration-creds";

    private readonly IDataProtector _protector;

    public DataProtectionIntegrationCredentialCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(string plainValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainValue);
        var bytes = Encoding.UTF8.GetBytes(plainValue);
        return _protector.Protect(bytes);
    }

    public string Decode(byte[] cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        if (cipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacío.", nameof(cipher));
        }
        var plain = _protector.Unprotect(cipher);
        return Encoding.UTF8.GetString(plain);
    }
}
