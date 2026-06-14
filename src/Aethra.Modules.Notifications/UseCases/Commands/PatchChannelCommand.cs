using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notifications.UseCases.Commands;

public sealed record PatchChannelCommand(
    string ChannelId,
    bool? IsActive,
    JsonElement? Config,
    IReadOnlyList<string>? EventFilters) : ICommand;

internal sealed class PatchChannelHandler(
    NotificationsDbContext db,
    INotificationConfigCodec codec,
    IClock clock) : ICommandHandler<PatchChannelCommand>
{
    public async Task<Result> Handle(PatchChannelCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ChannelId, out var parsed) || parsed.Value.Prefix != "nch")
        {
            return Error.Validation("channel.invalid_id", $"ChannelId invalido: '{request.ChannelId}'.");
        }
        var id = new NotificationChannelId(parsed.Value);
        var channel = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (channel is null)
        {
            return Error.NotFound("channel.not_found", $"Canal '{request.ChannelId}' no existe.");
        }

        var now = clock.UtcNow;

        // Si se provee config nuevo, validar su shape contra el TIPO del canal ANTES de mutar nada
        // (igual que CreateChannel). Antes se cifraba/guardaba sin validar → un patch podía dejar el
        // canal con config inválida y los envíos fallaban en silencio.
        if (request.Config is JsonElement cfg)
        {
            var configError = NotificationConfigShape.Validate(channel.Type, cfg);
            if (configError is not null)
            {
                return configError;
            }
            channel.UpdateConfig(codec.Encode(cfg.GetRawText()), now);
        }
        if (request.IsActive is bool active)
        {
            channel.SetActive(active, now);
        }
        if (request.EventFilters is not null)
        {
            try
            {
                channel.UpdateEventFilters(request.EventFilters, now);
            }
            catch (ArgumentException ex)
            {
                return Error.Validation("channel.invalid_filters", ex.Message);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
