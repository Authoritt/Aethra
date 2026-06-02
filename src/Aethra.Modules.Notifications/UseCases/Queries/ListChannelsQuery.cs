using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Modules.Notifications.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notifications.UseCases.Queries;

public sealed record ListChannelsQuery() : IQuery<IReadOnlyList<NotificationChannelDto>>;

internal sealed class ListChannelsHandler(NotificationsDbContext db, INotificationConfigCodec codec)
    : IQueryHandler<ListChannelsQuery, IReadOnlyList<NotificationChannelDto>>
{
    public async Task<Result<IReadOnlyList<NotificationChannelDto>>> Handle(ListChannelsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.NotificationChannels
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var list = new List<NotificationChannelDto>(rows.Count);
        foreach (var row in rows)
        {
            // El config va sanitizado: secrets se ocultan. Slack/Discord webhook_url y telegram
            // bot_token se reemplazan por su forma redacted antes de devolver al cliente.
            JsonElement? sanitized = null;
            try
            {
                var raw = codec.Decode(row.ConfigCipher);
                sanitized = SanitizeConfig(row.Type, raw);
            }
            catch
            {
                // Si el DataProtection key se rotó/perdió, mostramos null en config — el operador
                // puede borrar y recrear el canal.
            }

            list.Add(new NotificationChannelDto(
                row.Id.ToString(),
                row.Name,
                row.Type.ToString(),
                row.IsActive,
                row.EventFilters.ToList(),
                sanitized,
                row.CreatedAt,
                row.UpdatedAt,
                row.LastDeliveredAt));
        }

        return list;
    }

    private static JsonElement SanitizeConfig(NotificationChannelType type, string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            // Heuristica: cualquier propiedad cuyo nombre contenga "token", "password", "secret" o
            // "url" (webhooks) se enmascara en la respuesta.
            if (IsSecretField(prop.Name))
            {
                dict[prop.Name] = MaskValue(prop.Value);
            }
            else
            {
                dict[prop.Name] = prop.Value.Clone();
            }
        }
        return JsonSerializer.SerializeToElement(dict);
    }

    private static bool IsSecretField(string fieldName)
    {
        return fieldName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("password", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("webhook_url", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("webhookUrl", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskValue(JsonElement el)
    {
        var s = el.ValueKind == JsonValueKind.String ? el.GetString() ?? string.Empty : el.ToString();
        if (s.Length <= 8) { return "********"; }
        return s[..4] + "***" + s[^2..];
    }
}
