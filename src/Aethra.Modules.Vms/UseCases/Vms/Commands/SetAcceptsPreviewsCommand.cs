using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

/// <summary>
/// F12.3 — opt-in / opt-out de una VM al pool de previews. Apagar no migra Instances ya
/// hospedadas; solo afecta los próximos PRs.
/// </summary>
public sealed record SetAcceptsPreviewsCommand(string VmId, bool AcceptsPreviews) : ICommand;

internal sealed class SetAcceptsPreviewsHandler(VmsDbContext db, IClock clock)
    : ICommandHandler<SetAcceptsPreviewsCommand>
{
    public async Task<Result> Handle(SetAcceptsPreviewsCommand request, CancellationToken cancellationToken)
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
        vm.SetAcceptsPreviews(request.AcceptsPreviews, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
