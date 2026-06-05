using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Clients.Commands;

/// <summary>
/// Actualiza la información administrativa de un <c>Client</c> (display name, descripción, email de
/// contacto, billing tag). El <see cref="Client.Slug"/> NO se puede cambiar (rompería container names
/// ya desplegados). Reutiliza <see cref="Client.UpdateInfo"/>.
/// </summary>
public sealed record UpdateClientCommand(
    string ClientId,
    string DisplayName,
    string? Description,
    string? ContactEmail,
    string? BillingTag) : ICommand;

public sealed class UpdateClientValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientValidator()
    {
        RuleFor(c => c.ClientId).NotEmpty();
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.ContactEmail).EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.ContactEmail));
        RuleFor(c => c.BillingTag).MaximumLength(64);
    }
}

internal sealed class UpdateClientHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<UpdateClientCommand>
{
    public async Task<Result> Handle(UpdateClientCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ClientId, out var parsed) || parsed.Value.Prefix != "cli")
        {
            return Error.Validation("client.invalid_id", "ID de client inválido.");
        }
        var clientId = new ClientId(parsed.Value);

        var client = await db.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken)
            .ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound("client.not_found", $"Client '{request.ClientId}' no existe.");
        }

        try
        {
            client.UpdateInfo(
                request.DisplayName,
                request.Description,
                request.ContactEmail,
                request.BillingTag,
                clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("client.invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
