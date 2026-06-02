using System.Text;
using Aethra.Modules.Notifications.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Notifications.Infrastructure.Security;

/// <summary>
/// Codec basado en DataProtection (mismo patron que
/// <c>Aethra.Modules.Notes.Infrastructure.Security.DataProtectionPinnedFactCodec</c>).
/// Purpose dedicado <c>aethra-notification-config</c> — si se compromete otro purpose,
/// los configs de canales siguen ilegibles.
/// </summary>
public sealed class DataProtectionNotificationConfigCodec : INotificationConfigCodec
{
    private const string Purpose = "aethra-notification-config";

    private readonly IDataProtector _protector;

    public DataProtectionNotificationConfigCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(string plainJson)
    {
        ArgumentNullException.ThrowIfNull(plainJson);
        var raw = Encoding.UTF8.GetBytes(plainJson);
        return _protector.Protect(raw);
    }

    public string Decode(byte[] cipher)
    {
        if (cipher is null || cipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacio.", nameof(cipher));
        }
        var raw = _protector.Unprotect(cipher);
        return Encoding.UTF8.GetString(raw);
    }
}
