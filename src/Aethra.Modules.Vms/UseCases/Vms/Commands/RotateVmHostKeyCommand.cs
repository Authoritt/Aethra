using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

public sealed record RotateVmHostKeyCommand(string VmId, string NewFingerprint) : ICommand<VmDto>;

public sealed class RotateVmHostKeyValidator : AbstractValidator<RotateVmHostKeyCommand>
{
    public RotateVmHostKeyValidator()
    {
        RuleFor(x => x.VmId).NotEmpty();
        RuleFor(x => x.NewFingerprint)
            .NotEmpty()
            .Must(SshHostKeyHelper.IsValidFingerprint)
            .WithMessage("NewFingerprint debe ser una huella SHA256 válida (formato 'SHA256:<base64>' o base64 de 32 bytes).");
    }
}

internal sealed class RotateVmHostKeyHandler(VmsDbContext db, IClock clock)
    : ICommandHandler<RotateVmHostKeyCommand, VmDto>
{
    public async Task<Result<VmDto>> Handle(RotateVmHostKeyCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            return Error.Validation("vm.invalid_id", "ID de VM inválido.");
        }
        var typedId = new VmId(parsed.Value);
        var vm = await db.Vms.FirstOrDefaultAsync(v => v.Id == typedId, cancellationToken);
        if (vm is null)
        {
            return Error.NotFound("vm.not_found", $"No existe la VM '{request.VmId}'.");
        }

        vm.RotateSshHostKeyFingerprint(request.NewFingerprint, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return VmMapper.ToDto(vm);
    }
}
