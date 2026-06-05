using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

/// <summary>
/// Actualiza la metadata editable de una VM (nombre, IPs, descripción). El slug es inmutable.
/// </summary>
public sealed record UpdateVmCommand(
    string VmId,
    string Name,
    string? PublicIp,
    string? PrivateIp,
    string? Description) : ICommand;

public sealed class UpdateVmValidator : AbstractValidator<UpdateVmCommand>
{
    public UpdateVmValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
    }
}

internal sealed class UpdateVmHandler(VmsDbContext db, IClock clock)
    : ICommandHandler<UpdateVmCommand>
{
    public async Task<Result> Handle(UpdateVmCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            return Error.Validation("vm.invalid_id", "ID de VM inválido.");
        }
        var typedId = new VmId(parsed.Value);
        var vm = await db.Vms.FirstOrDefaultAsync(v => v.Id == typedId, cancellationToken).ConfigureAwait(false);
        if (vm is null)
        {
            return Error.NotFound("vm.not_found", $"VM '{request.VmId}' no existe.");
        }

        vm.UpdateMetadata(request.Name, request.PublicIp, request.PrivateIp, request.Description, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
