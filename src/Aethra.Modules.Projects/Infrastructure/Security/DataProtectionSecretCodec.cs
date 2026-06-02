using System.Text;
using Aethra.Modules.Projects.Domain.Secrets;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Projects.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="ISecretCodec"/> sobre ASP.NET DataProtection.
/// Purpose: <c>aethra-secrets</c> — aislado del purpose de webhooks (<c>aethra-webhook-secrets</c>)
/// para que comprometer uno no exponga el otro.
///
/// Los valores se persisten cifrados en la columna <c>value_cipher</c> (bytea) de la tabla
/// <c>projects.secrets</c> y se descifran en memoria sólo en el orquestador de deploy, justo
/// antes de pasarlos al satélite como env vars de runtime.
/// </summary>
public sealed class DataProtectionSecretCodec : ISecretCodec
{
    private const string Purpose = "aethra-secrets";

    private readonly IDataProtector _protector;

    public DataProtectionSecretCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(string plainValue)
    {
        ArgumentNullException.ThrowIfNull(plainValue);
        return _protector.Protect(Encoding.UTF8.GetBytes(plainValue));
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
