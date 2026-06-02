namespace Aethra.Modules.Projects.Domain.Templates;

/// <summary>
/// Codec para cifrar/descifrar el <c>WebhookSecret</c> de un <see cref="Template"/>.
/// GitHub envía el header <c>X-Hub-Signature-256</c> con un HMAC-SHA256 del body usando el
/// secret en plaintext, así que necesitamos descifrarlo en memoria al validar. Hashearlo
/// (como un password) NO funciona — esto es cifrado simétrico estilo DataProtection.
/// La implementación productiva (<c>DataProtectionWebhookSecretCodec</c>) usa el purpose
/// <c>aethra-webhook-secrets</c>.
/// </summary>
public interface IWebhookSecretCodec
{
    /// <summary>Cifra el secret en plaintext. El cipher se persiste en la columna bytea.</summary>
    byte[] Encode(string plainSecret);

    /// <summary>Descifra el cipher devolviendo el secret en plaintext (sólo en memoria).</summary>
    string Decode(byte[] cipher);
}
