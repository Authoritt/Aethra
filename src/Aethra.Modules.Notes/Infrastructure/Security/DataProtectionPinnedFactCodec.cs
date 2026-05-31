using System.Text;
using Aethra.Modules.Notes.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Notes.Infrastructure.Security;

/// <summary>
/// Codec basado en DataProtection (mismo enfoque que
/// <c>Aethra.Modules.Services.Infrastructure.Provisioning.DataProtectionAdminCredentialsCodec</c>).
/// Purpose dedicado <c>aethra-pinned-facts</c>: comprometer otro purpose no expone estos valores.
///
/// Las llaves DataProtection se persisten desde <c>Program.cs</c> en
/// <c>DataProtection:KeyDir</c>; si se pierden, los pinned facts cifrados quedan ilegibles.
/// </summary>
public sealed class DataProtectionPinnedFactCodec : IPinnedFactCodec
{
    private const string Purpose = "aethra-pinned-facts";

    private readonly IDataProtector _protector;

    public DataProtectionPinnedFactCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(string plainValue)
    {
        ArgumentNullException.ThrowIfNull(plainValue);
        var raw = Encoding.UTF8.GetBytes(plainValue);
        return _protector.Protect(raw);
    }

    public string Decode(byte[] cipher)
    {
        if (cipher is null || cipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacío.", nameof(cipher));
        }
        var raw = _protector.Unprotect(cipher);
        return Encoding.UTF8.GetString(raw);
    }
}
