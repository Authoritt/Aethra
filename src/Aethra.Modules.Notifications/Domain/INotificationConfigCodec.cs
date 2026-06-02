namespace Aethra.Modules.Notifications.Domain;

/// <summary>
/// Codec para cifrar/descifrar el JSON de configuracion de cada canal. La implementacion
/// usa DataProtection con purpose <c>aethra-notification-config</c>.
/// </summary>
public interface INotificationConfigCodec
{
    /// <summary>Cifra un JSON plano y devuelve el blob persistible.</summary>
    byte[] Encode(string plainJson);

    /// <summary>Descifra el blob y devuelve el JSON plano.</summary>
    string Decode(byte[] cipher);
}
