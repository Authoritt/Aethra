using System.ComponentModel;
using System.Text.Json;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.UseCases.Commands;
using Aethra.Modules.Notifications.UseCases.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// F11.5 — herramientas para gestionar Notification Channels (Slack/Discord/Telegram/Email/Webhook)
/// y leer el historial de deliveries. Reutiliza los handlers del módulo Notifications.
///
/// <para>
/// Config shapes esperados por <c>type</c>:
/// <list type="bullet">
///   <item>Slack/Discord: <c>{ "webhook_url": "https://..." }</c></item>
///   <item>Telegram: <c>{ "bot_token": "...", "chat_id": "-100..." }</c></item>
///   <item>Email: <c>{ "smtp_credential_name": "...", "from": "ops@empresa.com", "to": "alerts@empresa.com" }</c></item>
///   <item>Webhook: <c>{ "url": "https://...", "http_method": "POST" }</c></item>
/// </list>
/// El backend cifra el config con DataProtection y nunca lo devuelve plaintext en list.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class NotificationsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_create_notification_channel", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un canal de notificación. config es un dict JSON; el shape depende del type. " +
        "event_filters opcional (default: todos los eventos). Devuelve {id,name,type,...}.")]
    public async Task<object> CreateChannelAsync(
        [Description("Nombre único legible (ej. 'Slack - Alertas Prod'). Max 100 chars.")] string name,
        [Description("Tipo: 'Slack', 'Discord', 'Telegram', 'Email' o 'Webhook'.")] string type,
        [Description("Dict JSON con la config (shape depende del type; ver doc XML del tool-type).")] JsonElement config,
        [Description("Lista de event types a filtrar (ej. ['deploy.failed','monitor.down']). Vacío = todos.")] IReadOnlyList<string>? eventFilters,
        [Description("Si true, NO crea — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotificationsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.NotificationsWrite);
        }
        if (!Enum.TryParse<NotificationChannelType>(type, ignoreCase: true, out var parsedType))
        {
            return McpResponses.Failure("channel.unknown_type",
                $"type='{type}' inválido. Use Slack, Discord, Telegram, Email o Webhook.", "validation");
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: "POST /api/notifications/channels",
                plan: new { name, type = parsedType.ToString(), event_filters = eventFilters ?? [], config_keys = ListConfigKeys(config) });
        }
        var result = await mediator.Send(
            new CreateChannelCommand(name, parsedType, config, eventFilters), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: result.Value,
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_test_notification_channel",
                    Why: "Mandá una notificación de prueba para verificar que el webhook/credentials están bien.",
                    SuggestedArgs: new { channel_id = result.Value.Id }),
            ]);
    }

    [McpServerTool(Name = "aethra_test_notification_channel", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Manda una notificación de prueba al canal. Crea un delivery (Pending → Sent/Failed) " +
        "que queda registrado en el historial. Útil tras crear el canal.")]
    public async Task<object> TestChannelAsync(
        [Description("ID del canal (formato 'nch_...').")] string channelId,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotificationsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.NotificationsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"POST /api/notifications/channels/{channelId}/test",
                plan: new { channelId, action = "send test notification" });
        }
        var result = await mediator.Send(new TestChannelCommand(channelId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: result.Value,
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_notification_deliveries",
                    Why: "Mirá el delivery resultante en el historial (estado Sent o Failed).",
                    SuggestedArgs: new { channel_id = channelId, limit = 5 }),
            ]);
    }

    [McpServerTool(Name = "aethra_list_notification_channels", ReadOnly = true, OpenWorld = false)]
    [Description("Lista todos los canales con su config (secrets enmascarados). Read-only.")]
    public async Task<object> ListChannelsAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotificationsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.NotificationsRead);
        }
        var result = await mediator.Send(new ListChannelsQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_list_notification_deliveries", ReadOnly = true, OpenWorld = false)]
    [Description("Lista deliveries históricos. Filtros opcionales por channel_id y status ('Pending','Sent','Failed').")]
    public async Task<object> ListDeliveriesAsync(
        [Description("Filtrar por channel id (formato 'nch_...'). Omitir para todos.")] string? channelId,
        [Description("Filtrar por status: 'Pending','Sent','Failed'. Omitir para todos.")] string? status,
        [Description("Cantidad máxima (1..500). Default 50.")] int? limit,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotificationsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.NotificationsRead);
        }
        var effectiveLimit = limit ?? 50;
        var result = await mediator.Send(
            new ListDeliveriesQuery(channelId, status, effectiveLimit), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_notification_channel", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Elimina un canal de notificación: detiene los envíos futuros por ese canal. El historial de "
        + "deliveries ya registrado NO se borra (consultable con aethra_list_notification_deliveries). "
        + "Usá dry_run=true primero para confirmar.")]
    public async Task<object> DeleteChannelAsync(
        [Description("ID del canal (formato 'nch_...'; lo lista aethra_list_notification_channels).")] string channelId,
        [Description("Si true, NO borra — devuelve el plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotificationsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.NotificationsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"DELETE /api/notifications/channels/{channelId}",
                plan: new { channelId, action = "delete notification channel (stops future sends; keeps delivery history)" });
        }
        var result = await mediator.Send(new DeleteChannelCommand(channelId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { channel_id = channelId, deleted = true })
            : McpResponses.FromError(result.Error);
    }

    private static List<string> ListConfigKeys(JsonElement config)
    {
        var keys = new List<string>();
        if (config.ValueKind != JsonValueKind.Object) { return keys; }
        foreach (var prop in config.EnumerateObject())
        {
            keys.Add(prop.Name);
        }
        return keys;
    }
}
