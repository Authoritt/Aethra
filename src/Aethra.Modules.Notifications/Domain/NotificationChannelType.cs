namespace Aethra.Modules.Notifications.Domain;

/// <summary>
/// Tipo de canal de notificacion soportado. Cada tipo define su propio shape de configuracion
/// JSON cifrada en <c>NotificationChannel.ConfigCipher</c>:
/// <list type="bullet">
///   <item>Slack: <c>{ webhook_url }</c></item>
///   <item>Discord: <c>{ webhook_url }</c></item>
///   <item>Telegram: <c>{ bot_token, chat_id }</c></item>
///   <item>Email: <c>{ smtp_credential_name, from, to }</c> (smtp_credential_name refiere a IntegrationCredential)</item>
///   <item>Webhook: <c>{ url, http_method, headers? }</c></item>
/// </list>
/// </summary>
public enum NotificationChannelType
{
    Slack = 0,
    Discord = 1,
    Telegram = 2,
    Email = 3,
    Webhook = 4,
}
