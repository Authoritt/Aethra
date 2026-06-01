namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Cifra/descifra el valor en texto plano de una <see cref="IntegrationCredential"/>.
/// La interfaz vive en Domain porque el aggregate la recibe en <see cref="IntegrationCredential.Create"/>
/// y <see cref="IntegrationCredential.Rotate"/>; la implementación concreta basada en
/// DataProtection vive en Infrastructure (no se puede ahí porque Domain debe ser BCL-only).
/// </summary>
public interface IIntegrationCredentialCodec
{
    /// <summary>
    /// Cifra el valor en texto plano con el purpose de DataProtection del módulo.
    /// </summary>
    byte[] Encode(string plainValue);

    /// <summary>
    /// Descifra el blob persistido. Lanza <see cref="System.Security.Cryptography.CryptographicException"/>
    /// si la key de DataProtection ya no existe o el blob fue manipulado.
    /// </summary>
    string Decode(byte[] cipher);
}
