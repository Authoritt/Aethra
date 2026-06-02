using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notifications.UseCases.Commands;

public sealed record DeleteChannelCommand(string ChannelId) : ICommand;

internal sealed class DeleteChannelHandler(NotificationsDbContext db) : ICommandHandler<DeleteChannelCommand>
{
    public async Task<Result> Handle(DeleteChannelCommand request, CancellationToken cancellationToken)
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

        channel.MarkDeleted();
        db.NotificationChannels.Remove(channel);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
