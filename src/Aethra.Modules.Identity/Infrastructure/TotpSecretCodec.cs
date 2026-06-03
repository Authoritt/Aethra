using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// F12.1B — codec para encrypt-at-rest de los secretos TOTP de cada usuario. Usa
/// DataProtection con purpose dedicado <c>aethra-totp-secrets</c> aislado del resto de
/// purposes (passwords, webhooks, etc.). Si la BD se filtra sin las llaves de DataProtection,
/// los secretos TOTP quedan ilegibles.
/// </summary>
public interface ITotpSecretCodec
{
    byte[] Protect(byte[] secret);
    byte[] Unprotect(byte[] cipher);
}

internal sealed class DataProtectionTotpSecretCodec : ITotpSecretCodec
{
    private const string Purpose = "aethra-totp-secrets";
    private readonly IDataProtector _protector;

    public DataProtectionTotpSecretCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Protect(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return _protector.Protect(secret);
    }

    public byte[] Unprotect(byte[] cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        return _protector.Unprotect(cipher);
    }
}
