using System.Text;
using Aethra.Modules.Projects.Domain.Templates;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Projects.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="IWebhookSecretCodec"/> sobre ASP.NET DataProtection.
/// Purpose: <c>aethra-webhook-secrets</c> — comprometer otro purpose no expone estos secretos.
///
/// El secret de un <see cref="Template"/> es el shared secret usado por GitHub para firmar
/// HMAC-SHA256 los webhooks. Persiste cifrado en la columna <c>webhook_secret_cipher</c>
/// (bytea) y se descifra en memoria sólo al validar la firma de un payload entrante.
/// </summary>
public sealed class DataProtectionWebhookSecretCodec : IWebhookSecretCodec
{
    private const string Purpose = "aethra-webhook-secrets";

    private readonly IDataProtector _protector;

    public DataProtectionWebhookSecretCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(string plainSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainSecret);
        return _protector.Protect(Encoding.UTF8.GetBytes(plainSecret));
    }

    public string Decode(byte[] cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        if (cipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacío.", nameof(cipher));
        }
        return Encoding.UTF8.GetString(_protector.Unprotect(cipher));
    }
}
