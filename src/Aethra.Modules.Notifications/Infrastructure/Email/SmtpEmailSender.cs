using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Aethra.Shared.Contracts.Settings;

namespace Aethra.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Implementacion SMTP usando <see cref="System.Net.Mail.SmtpClient"/> built-in. Apto para
/// flujos transaccionales de bajo volumen (notificaciones operativas). Si Aethra necesita en
/// el futuro features avanzados (TLS estricto, OAuth, ESMTP), migrar a MailKit.
///
/// Lee la credencial via <see cref="IIntegrationCredentialResolver"/>: el secret debe ser un
/// JSON con shape <see cref="SmtpCredentialConfig"/>. La credencial NO se cachea — se resuelve
/// por cada envio para soportar rotaciones sin reiniciar el proceso.
/// </summary>
public sealed class SmtpEmailSender(IIntegrationCredentialResolver resolver) : IEmailSender
{
    public async Task SendAsync(
        string smtpCredentialName,
        string from,
        string to,
        string subject,
        string body,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(smtpCredentialName);
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentException.ThrowIfNullOrEmpty(to);

        var rawSecret = await resolver.GetSecretAsync(smtpCredentialName, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Credencial SMTP '{smtpCredentialName}' no existe en Settings.");

        var cfg = JsonSerializer.Deserialize<SmtpCredentialConfig>(rawSecret)
            ?? throw new InvalidOperationException(
                $"Credencial SMTP '{smtpCredentialName}' tiene shape invalido (JSON malformado).");

        using var message = new MailMessage(from, to, subject, body);
        using var client = new SmtpClient(cfg.Host, cfg.Port)
        {
            EnableSsl = cfg.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(cfg.Username, cfg.Password),
            Timeout = 30_000,
        };

        await client.SendMailAsync(message, ct).ConfigureAwait(false);
    }
}
