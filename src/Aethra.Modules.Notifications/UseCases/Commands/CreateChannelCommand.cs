using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Modules.Notifications.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notifications.UseCases.Commands;

public sealed record CreateChannelCommand(
    string Name,
    NotificationChannelType Type,
    JsonElement Config,
    IReadOnlyList<string>? EventFilters) : ICommand<NotificationChannelDto>;

public sealed class CreateChannelValidator : AbstractValidator<CreateChannelCommand>
{
    public CreateChannelValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
    }
}

internal sealed class CreateChannelHandler(
    NotificationsDbContext db,
    INotificationConfigCodec codec,
    IClock clock)
    : ICommandHandler<CreateChannelCommand, NotificationChannelDto>
{
    public async Task<Result<NotificationChannelDto>> Handle(CreateChannelCommand request, CancellationToken cancellationToken)
    {
        if (await db.NotificationChannels.AnyAsync(c => c.Name == request.Name.Trim(), cancellationToken)
            .ConfigureAwait(false))
        {
            return Error.Conflict("channel.name_taken", $"Ya existe un canal '{request.Name}'.");
        }

        var validationError = NotificationConfigShape.Validate(request.Type, request.Config);
        if (validationError is not null)
        {
            return validationError;
        }

        var cipher = codec.Encode(request.Config.GetRawText());
        NotificationChannel channel;
        try
        {
            channel = NotificationChannel.Create(
                request.Name, request.Type, cipher, request.EventFilters, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("channel.invalid", ex.Message);
        }

        db.NotificationChannels.Add(channel);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new NotificationChannelDto(
            channel.Id.ToString(),
            channel.Name,
            channel.Type.ToString(),
            channel.IsActive,
            channel.EventFilters.ToList(),
            null, // No devolver config en respuesta de create — el operador ya la envio.
            channel.CreatedAt,
            channel.UpdatedAt,
            channel.LastDeliveredAt);
    }
}
