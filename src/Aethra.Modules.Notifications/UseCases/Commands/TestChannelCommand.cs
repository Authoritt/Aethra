using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Modules.Notifications.Infrastructure.Dispatch;
using Aethra.Modules.Notifications.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notifications.UseCases.Commands;

public sealed record TestChannelCommand(string ChannelId) : ICommand<TestChannelResultDto>;

internal sealed class TestChannelHandler(
    NotificationsDbContext db,
    NotificationDispatcher dispatcher,
    IClock clock)
    : ICommandHandler<TestChannelCommand, TestChannelResultDto>
{
    public async Task<Result<TestChannelResultDto>> Handle(TestChannelCommand request, CancellationToken cancellationToken)
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

        var payload = JsonSerializer.Serialize(new
        {
            test = true,
            message = "Aethra test notification",
            sent_at = clock.UtcNow,
        });
        var delivery = NotificationDelivery.Queue(channel.Id, "test", payload, clock.UtcNow);

        // Persistimos primero como Pending para tener trazabilidad en deliveries history.
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Intento directo, sin esperar al poll del dispatcher BackgroundService.
        var (ok, error) = await dispatcher.TryDeliverOneAsync(delivery, channel, cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        if (ok)
        {
            delivery.MarkSent(now);
            channel.MarkDelivered(now);
        }
        else
        {
            delivery.MarkPermanentlyFailed(error ?? "unknown", now);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new TestChannelResultDto(ok, error, now);
    }
}
