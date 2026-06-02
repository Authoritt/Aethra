using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Backup;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Services.UseCases.Backups;

public sealed record RunBackupCommand(string ServiceId) : ICommand<ServiceBackupDto>;

internal sealed class RunBackupHandler(BackupOrchestrator orchestrator)
    : ICommandHandler<RunBackupCommand, ServiceBackupDto>
{
    public async Task<Result<ServiceBackupDto>> Handle(RunBackupCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.Validation("service.invalid_id", $"ServiceId invalido: '{request.ServiceId}'.");
        }
        var id = new ManagedServiceId(parsed.Value);
        var r = await orchestrator.RunBackupAsync(id, cancellationToken).ConfigureAwait(false);
        if (r.IsFailure)
        {
            return r.Error;
        }
        var b = r.Value;
        return new ServiceBackupDto(
            b.Id.ToString(),
            b.ServiceId.ToString(),
            b.StartedAt,
            b.FinishedAt,
            b.Status.ToString(),
            b.SizeBytes,
            b.DestinationPath,
            b.ErrorMessage);
    }
}
