namespace Aethra.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Sender de email via SMTP. La implementacion lee la credencial referenciada por nombre via
/// <c>IIntegrationCredentialResolver</c> (secret JSON: <c>{ host, port, username, password, useTls }</c>).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(
        string smtpCredentialName,
        string from,
        string to,
        string subject,
        string body,
        CancellationToken ct);
}

public sealed record SmtpCredentialConfig(
    string Host,
    int Port,
    string Username,
    string Password,
    bool UseTls);
