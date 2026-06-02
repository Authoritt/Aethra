using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Backup;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Services.UseCases.Backups;

public sealed record RestoreBackupCommand(string ServiceId, string BackupId) : ICommand;

internal sealed class RestoreBackupHandler(BackupOrchestrator orchestrator) : ICommandHandler<RestoreBackupCommand>
{
    public async Task<Result> Handle(RestoreBackupCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ServiceId, out var sp) || sp.Value.Prefix != "svc")
        {
            return Error.Validation("service.invalid_id", $"ServiceId invalido: '{request.ServiceId}'.");
        }
        if (!AethraId.TryParse(request.BackupId, out var bp) || bp.Value.Prefix != "bkp")
        {
            return Error.Validation("backup.invalid_id", $"BackupId invalido: '{request.BackupId}'.");
        }
        return await orchestrator.RunRestoreAsync(
            new ManagedServiceId(sp.Value),
            new ServiceBackupId(bp.Value),
            cancellationToken).ConfigureAwait(false);
    }
}
