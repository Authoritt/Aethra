using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Backup;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Services.UseCases.Backups;

public sealed record DeleteBackupCommand(string BackupId) : ICommand;

internal sealed class DeleteBackupHandler(BackupOrchestrator orchestrator) : ICommandHandler<DeleteBackupCommand>
{
    public async Task<Result> Handle(DeleteBackupCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.BackupId, out var bp) || bp.Value.Prefix != "bkp")
        {
            return Error.Validation("backup.invalid_id", $"BackupId invalido: '{request.BackupId}'.");
        }
        return await orchestrator.DeleteBackupAsync(
            new ServiceBackupId(bp.Value),
            cancellationToken).ConfigureAwait(false);
    }
}
