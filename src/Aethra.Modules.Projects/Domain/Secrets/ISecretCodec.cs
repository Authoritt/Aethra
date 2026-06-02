namespace Aethra.Modules.Projects.Domain.Secrets;

/// <summary>
/// Codec para cifrar/descifrar valores de <see cref="Secret"/> at-rest. Cifrado simétrico
/// estilo DataProtection (no hash): el orquestador de deploy necesita el plaintext en memoria
/// para inyectarlo como env var/secret en el satélite. La implementación productiva
/// (<c>DataProtectionSecretCodec</c>) usa el purpose <c>aethra-secrets</c> — distinto del de
/// webhooks para que comprometer uno no exponga el otro.
/// </summary>
public interface ISecretCodec
{
    /// <summary>Cifra el secret en plaintext. El cipher se persiste en la columna bytea.</summary>
    byte[] Encode(string plainValue);

    /// <summary>Descifra el cipher devolviendo el plaintext (sólo en memoria, nunca persiste).</summary>
    string Decode(byte[] cipher);
}
