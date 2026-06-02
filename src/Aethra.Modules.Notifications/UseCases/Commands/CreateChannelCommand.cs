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

        var validationError = ValidateConfigShape(request.Type, request.Config);
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

    private static Error? ValidateConfigShape(NotificationChannelType type, JsonElement config)
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
