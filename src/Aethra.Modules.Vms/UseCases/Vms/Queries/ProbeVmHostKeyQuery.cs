using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.Infrastructure.Provisioning;
using Aethra.Modules.Vms.Infrastructure.Security;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Queries;

public sealed record ProbeVmHostKeyQuery(
    string? VmId = null,
    string? Host = null,
    int? Port = null) : IQuery<ProbeVmHostKeyResult>;

public sealed record ProbeVmHostKeyResult(
    int Port,
    string? HostKeyType,
    string? Fingerprint,
    bool MatchesPinned,
    string? PinnedFingerprint,
    bool Success,
    string? Warning = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

internal sealed class ProbeVmHostKeyHandler(
    VmsDbContext db,
    ISshHostKeyProber prober,
    ISshCredentialsCodec codec) : IQueryHandler<ProbeVmHostKeyQuery, ProbeVmHostKeyResult>
{
    public const string TofuWarningNotice =
        "La huella debe confirmarse por otro canal (consola del proveedor, ssh-keygen -lf en la VM): el sondeo es TOFU por la misma red.";

    public async Task<Result<ProbeVmHostKeyResult>> Handle(ProbeVmHostKeyQuery request, CancellationToken ct)
    {
        string? targetHost = null;
        int targetPort = 22;
        string? pinnedFingerprint = null;

        if (!string.IsNullOrWhiteSpace(request.VmId))
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

            pinnedFingerprint = vm.SshHostKeyFingerprint;

            // B1 (b): una VM registrada usa SOLO sus datos guardados (sin override de host/puerto desde
            // la peticion). Las credenciales guardadas mandan sobre PublicIp/PrivateIp: son el endpoint
            // SSH real (host Y puerto) contra el que se fijo la huella y al que va el reinstall; sondear
            // PublicIp:22 daria un "no coincide" falso en una VM con SSH en otro puerto. Solo se usan
            // host y puerto: el secreto descifrado no sale de aqui.
            if (vm.SshCredentialsCipher is not null)
            {
                var saved = codec.Decode(vm.SshCredentialsCipher);
                targetHost = saved.Host;
                targetPort = saved.Port;
            }
            else
            {
                targetHost = vm.PublicIp ?? vm.PrivateIp;
            }
        }
        else
        {
            targetHost = request.Host;
            targetPort = request.Port ?? 22;
        }

        if (string.IsNullOrWhiteSpace(targetHost))
        {
            return Error.Validation("ssh.host_required", "Se requiere 'host' o una VM con IP o credenciales registradas para sondear.");
        }

        var probeResult = await prober.ProbeAsync(targetHost, targetPort, ct);
        if (!probeResult.Success)
        {
            return new ProbeVmHostKeyResult(
                Port: targetPort,
                HostKeyType: null,
                Fingerprint: null,
                MatchesPinned: false,
                PinnedFingerprint: pinnedFingerprint,
                Success: false,
                Warning: TofuWarningNotice,
                ErrorCode: probeResult.ErrorCode,
                ErrorMessage: probeResult.ErrorMessage);
        }

        bool matches = pinnedFingerprint is not null &&
            SshHostKeyHelper.FingerprintsMatch(pinnedFingerprint, probeResult.Fingerprint);

        return new ProbeVmHostKeyResult(
            Port: targetPort,
            HostKeyType: probeResult.HostKeyType,
            Fingerprint: probeResult.Fingerprint,
            MatchesPinned: matches,
            PinnedFingerprint: pinnedFingerprint,
            Success: true,
            Warning: TofuWarningNotice);
    }
}
