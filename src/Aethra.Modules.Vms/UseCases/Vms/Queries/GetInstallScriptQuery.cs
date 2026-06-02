using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.Infrastructure.Provisioning;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aethra.Modules.Vms.UseCases.Vms.Queries;

/// <summary>
/// Devuelve el bash one-liner para instalar el satélite manualmente. Rota el token
/// (porque el script lleva el token embebido) — si el operador llama esto pero luego no
/// usa el script, el token anterior queda inválido. Eso es intencional: viene del mismo
/// principio que el endpoint <c>POST /api/vms</c> que rota una sola vez.
/// </summary>
public sealed record GetInstallScriptQuery(
    string VmId,
    string ContainerRuntime = "docker",
    bool InstallContainerRuntime = false) : IQuery<InstallScriptDto>;

public sealed record InstallScriptDto(
    string Script,
    IReadOnlyList<string> Lines,
    string TokenPlaintext);

internal sealed class GetInstallScriptHandler(
    VmsDbContext db,
    IClock clock,
    IConfiguration configuration) : IQueryHandler<GetInstallScriptQuery, InstallScriptDto>
{
    public async Task<Result<InstallScriptDto>> Handle(GetInstallScriptQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            return Error.Validation("vm.invalid_id", "ID de VM inválido.");
        }
        var typedId = new VmId(parsed.Value);
        var vm = await db.Vms.FirstOrDefaultAsync(v => v.Id == typedId, ct);
        if (vm is null)
        {
            return Error.NotFound("vm.not_found", $"No existe la VM '{request.VmId}'.");
        }
        var runtime = string.IsNullOrWhiteSpace(request.ContainerRuntime) ? "docker" : request.ContainerRuntime;
        if (runtime is not "docker" and not "podman")
        {
            return Error.Validation("vm.invalid_runtime", "ContainerRuntime debe ser 'docker' o 'podman'.");
        }
        var token = vm.RotateToken(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        var centralUrl = ResolveCentralUrl(configuration);
        var opts = new InstallOptions(centralUrl, token, runtime, request.InstallContainerRuntime);
        var script = ScriptBuilder.Build(opts);
        var lines = script.Split(' ');
        return new InstallScriptDto(script, lines, token);
    }

    private static string ResolveCentralUrl(IConfiguration configuration)
    {
        var explicitUrl = configuration["Aethra:CentralPublicUrl"];
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl.TrimEnd('/');
        }
        return "http://localhost:5000";
    }
}
