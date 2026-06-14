using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Kernel.Errors;

namespace Aethra.Modules.Notifications.UseCases.Commands;

/// <summary>
/// Valida el shape del config de un canal según su tipo (claves requeridas por Slack/Discord/Telegram/
/// Email/Webhook). Compartido por CreateChannel y PatchChannel para que un patch NO pueda dejar el canal
/// con un config que create habría rechazado (lo que haría fallar los envíos en silencio).
/// </summary>
internal static class NotificationConfigShape
{
    public static Error? Validate(NotificationChannelType type, JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return Error.Validation("channel.config_invalid", "Config debe ser un objeto JSON.");
        }

        bool HasNonEmptyString(string key) =>
            config.TryGetProperty(key, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(prop.GetString());

        return type switch
        {
            NotificationChannelType.Slack or NotificationChannelType.Discord =>
                HasNonEmptyString("webhook_url")
                    ? null
                    : Error.Validation("channel.config_invalid", "Falta 'webhook_url'."),
            NotificationChannelType.Telegram =>
                HasNonEmptyString("bot_token") && HasNonEmptyString("chat_id")
                    ? null
                    : Error.Validation("channel.config_invalid", "Faltan 'bot_token' y/o 'chat_id'."),
            NotificationChannelType.Email =>
                HasNonEmptyString("smtp_credential_name")
                && HasNonEmptyString("from")
                && HasNonEmptyString("to")
                    ? null
                    : Error.Validation("channel.config_invalid",
                        "Email requiere 'smtp_credential_name', 'from' y 'to'."),
            NotificationChannelType.Webhook =>
                HasNonEmptyString("url")
                    ? null
                    : Error.Validation("channel.config_invalid", "Falta 'url'."),
            _ => Error.Validation("channel.unknown_type", $"Tipo desconocido: {type}"),
        };
    }
}
