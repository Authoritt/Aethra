using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Queries;

/// <summary>Status de instalación + últimas 50 líneas del log.</summary>
public sealed record GetInstallStatusQuery(string VmId) : IQuery<InstallStatusDto>;

public sealed record InstallStatusDto(
    string VmId,
    string Status,
    DateTimeOffset? LastSeenAt,
    bool HasSavedCredentials,
    IReadOnlyList<string> LastLogLines);

internal sealed class GetInstallStatusHandler(VmsDbContext db) : IQueryHandler<GetInstallStatusQuery, InstallStatusDto>
{
    private const int MaxLines = 50;

    public async Task<Result<InstallStatusDto>> Handle(GetInstallStatusQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            return Error.Validation("vm.invalid_id", "ID de VM inválido.");
        }
        var typedId = new VmId(parsed.Value);
        var vm = await db.Vms.AsNoTracking().FirstOrDefaultAsync(v => v.Id == typedId, ct);
        if (vm is null)
        {
            return Error.NotFound("vm.not_found", $"No existe la VM '{request.VmId}'.");
        }
        var allLines = string.IsNullOrEmpty(vm.InstallLog)
            ? Array.Empty<string>()
            : vm.InstallLog.Split('\n', StringSplitOptions.None);
        var lastLines = allLines.Length > MaxLines
            ? allLines[(allLines.Length - MaxLines)..]
            : allLines;
        return new InstallStatusDto(
            VmId: request.VmId,
            Status: vm.InstallStatus.ToString(),
            LastSeenAt: vm.LastSeenAt,
            HasSavedCredentials: vm.SshCredentialsCipher is { Length: > 0 },
            LastLogLines: lastLines);
    }
}
