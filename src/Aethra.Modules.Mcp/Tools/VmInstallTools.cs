using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// F11.5 — herramientas para registrar VMs e instalar el satélite vía SSH desde el MCP.
///
/// <para>
/// <b>WARNING sobre secretos</b>: <see cref="ProvisionVmAsync"/> y <see cref="InstallSatelliteAsync"/>
/// reciben <c>ssh.key_or_password</c> como parámetro de tool — eso significa que el contenido
/// se transmite por el canal MCP y puede quedar en logs de auditoría del agente IA. Para evitarlo:
/// <list type="number">
///   <item>Usá <c>aethra_provision_vm</c> sin <c>ssh</c>, después <c>aethra_get_install_script</c>
///         para obtener el bash one-liner y pegalo manualmente en la VM.</item>
///   <item>O usá una clave SSH dedicada (que pueda revocar después) para esta sesión.</item>
/// </list>
/// Para mitigar el riesgo en logs, los redactores de transcript del agente deben buscar
/// fields que terminen en <c>password</c>, <c>key</c>, <c>value</c> (en el shape SSH) y
/// reemplazar con <c>***REDACTED***</c>.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class VmInstallTools(IMediator mediator, IMcpCallerContext caller)
{
    /// <summary>
    /// Input SSH usado por las tools <c>provision_vm</c> e <c>install_satellite</c>. Coincide con
    /// el shape del REST endpoint <c>POST /api/vms/{id}/install/auto</c>.
    /// </summary>
    public sealed record SshInput(
        [property: Description("Hostname o IP a conectar.")] string Host,
        [property: Description("Puerto SSH. Default 22.")] int Port,
        [property: Description("Usuario remoto (ej. 'root' o 'ubuntu').")] string User,
        [property: Description("Método de auth: 'key' (PEM private key) o 'password'.")] string AuthMethod,
        [property: Description("WARNING SECRETO: el PEM key o la password literal. Se transmite por MCP. " +
            "Considere obtener el script manual con aethra_get_install_script en su lugar.")] string KeyOrPassword,
        [property: Description("Si true, instala docker/podman en la VM (apt-get / yum). Default false.")] bool? InstallContainerRuntime,
        [property: Description("Container runtime: 'docker' o 'podman'. Default 'docker'.")] string? ContainerRuntime);

    [McpServerTool(Name = "aethra_provision_vm", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Registra una VM y opcionalmente dispara el install del satélite vía SSH (combo). " +
        "El token de satélite se devuelve UNA SOLA VEZ — guardalo o pasá ssh para que el central " +
        "lo inyecte automáticamente. WARNING: si pasás ssh.key_or_password, el secreto va por el canal MCP.")]
    public async Task<object> ProvisionVmAsync(
        [Description("Nombre humano (ej. 'oracle-arm-01').")] string name,
        [Description("Slug opcional (lowercase, a-z 0-9 -). Si null, se infiere del name.")] string? slug,
        [Description("IP pública opcional.")] string? publicIp,
        [Description("IP privada opcional.")] string? privateIp,
        [Description("Descripción libre opcional.")] string? description,
        [Description("Si presente, dispara también el install vía SSH. Si null, solo registra la VM.")] SshInput? ssh,
        [Description("Si true, NO crea — devuelve plan + script manual sugerido.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: ssh is null
                    ? "POST /api/vms (register only)"
                    : "POST /api/vms (register) → POST /api/vms/{id}/install/auto (install)",
                plan: new
                {
                    name,
                    slug,
                    publicIp,
                    privateIp,
                    description,
                    install_via_ssh = ssh is not null,
                    ssh_host = ssh?.Host,
                    ssh_user = ssh?.User,
                    ssh_auth_method = ssh?.AuthMethod,
                    ssh_secret = ssh is null ? null : "***REDACTED***",
                });
        }

        // Paso 1: registrar la VM (devuelve el token UNA VEZ).
        var register = await mediator.Send(
            new RegisterVmCommand(name, slug, publicIp, privateIp, description), ct).ConfigureAwait(false);
        if (!register.IsSuccess)
        {
            return McpResponses.FromError(register.Error);
        }
        var vmId = register.Value.VmId;
        var tokenOnce = register.Value.TokenPlaintext;

        // Paso 2 (opcional): si vino ssh, dispará el install.
        if (ssh is not null)
        {
            var sshInput = MapSsh(ssh);
            var installCmd = new AutoInstallSatelliteCommand(
                VmId: vmId,
                Credentials: sshInput,
                InstallContainerRuntime: ssh.InstallContainerRuntime ?? false,
                ContainerRuntime: string.IsNullOrWhiteSpace(ssh.ContainerRuntime) ? "docker" : ssh.ContainerRuntime!,
                DryRun: false);
            var install = await mediator.Send(installCmd, ct).ConfigureAwait(false);
            if (!install.IsSuccess)
            {
                // La VM ya quedó registrada — devolvemos eso + el error del install.
                return McpResponses.Failure(
                    code: install.Error.Code,
                    message: $"VM registrada ({vmId}) pero install falló: {install.Error.Message}. " +
                        "Probá aethra_install_satellite con los datos correctos, o aethra_get_install_script para hacerlo manual.",
                    type: install.Error.Type.ToString().ToLowerInvariant());
            }
            return McpResponses.OkWithNextActions(
                data: new
                {
                    vm_id = vmId,
                    slug = register.Value.Slug,
                    name = register.Value.Name,
                    token_revealed_once = tokenOnce,
                    install_status = install.Value.Status,
                    install_log_url = install.Value.InstallUrl,
                    stream_hub = install.Value.StreamHub,
                },
                nextActions:
                [
                    new McpResponses.NextAction(
                        Tool: "aethra_get_install_status",
                        Why: "Poll cada ~5s hasta status='Installed' (o 'Failed' para ver errores).",
                        SuggestedArgs: new { vm_id = vmId }),
                ]);
        }

        // Sin ssh: devolvemos token + script manual sugerido.
        return McpResponses.OkWithNextActions(
            data: new
            {
                vm_id = vmId,
                slug = register.Value.Slug,
                name = register.Value.Name,
                token_revealed_once = tokenOnce,
                install_status = (string?)null,
                manual_script = register.Value.InstallScript,
            },
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_get_install_script",
                    Why: "Obtené el bash one-liner para pegar manualmente en la VM (más seguro que mandar SSH key por MCP).",
                    SuggestedArgs: new { vm_id = vmId, container_runtime = "docker" }),
                new McpResponses.NextAction(
                    Tool: "aethra_install_satellite",
                    Why: "Alternativa: pasar credenciales SSH para que el central instale por vos.",
                    SuggestedArgs: new { vm_id = vmId, ssh = new { host = publicIp ?? "<ip>", port = 22, user = "root", auth_method = "key", key_or_password = "<pem>" } }),
            ]);
    }

    [McpServerTool(Name = "aethra_install_satellite", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Dispara el install del satélite en una VM ya registrada vía SSH. WARNING: la SSH key/password " +
        "se transmite por MCP. Considere aethra_get_install_script si el secreto en logs es problemático.")]
    public async Task<object> InstallSatelliteAsync(
        [Description("ID de la VM (formato 'vm_...').")] string vmId,
        [Description("Credenciales SSH + opciones de runtime.")] SshInput ssh,
        [Description("Si true, NO ejecuta — devuelve plan y script generado.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsWrite);
        }
        var sshInput = MapSsh(ssh);
        var cmd = new AutoInstallSatelliteCommand(
            VmId: vmId,
            Credentials: sshInput,
            InstallContainerRuntime: ssh.InstallContainerRuntime ?? false,
            ContainerRuntime: string.IsNullOrWhiteSpace(ssh.ContainerRuntime) ? "docker" : ssh.ContainerRuntime!,
            DryRun: dryRun);

        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }

        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"POST /api/vms/{vmId}/install/auto",
                plan: new
                {
                    vm_id = vmId,
                    ssh_host = ssh.Host,
                    ssh_user = ssh.User,
                    ssh_auth_method = ssh.AuthMethod,
                    ssh_secret = "***REDACTED***",
                    install_plan = result.Value.Plan,
                    install_script_preview = result.Value.Script,
                });
        }

        return McpResponses.OkWithNextActions(
            data: new
            {
                vm_id = vmId,
                status = result.Value.Status,
                install_url = result.Value.InstallUrl,
                stream_hub = result.Value.StreamHub,
            },
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_get_install_status",
                    Why: "Poll cada ~5s hasta status='Installed' o 'Failed'.",
                    SuggestedArgs: new { vm_id = vmId }),
            ]);
    }

    [McpServerTool(Name = "aethra_get_install_status", ReadOnly = true, OpenWorld = false)]
    [Description("Lee el estado de instalación del satélite + últimas 50 líneas del log. " +
        "Llamar periódicamente tras dispararse un install vía aethra_install_satellite / aethra_provision_vm.")]
    public async Task<object> GetInstallStatusAsync(
        [Description("ID de la VM (formato 'vm_...').")] string vmId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsRead);
        }
        var result = await mediator.Send(new GetInstallStatusQuery(vmId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_install_script", ReadOnly = false, OpenWorld = false)]
    [Description("Devuelve el bash one-liner para instalar el satélite manualmente. " +
        "OJO: este endpoint ROTA el token de la VM (porque va embebido en el script) — si no usás el script, " +
        "el token anterior queda inválido. Recomendado cuando preferís NO mandar credenciales SSH por MCP.")]
    public async Task<object> GetInstallScriptAsync(
        [Description("ID de la VM (formato 'vm_...').")] string vmId,
        [Description("Container runtime: 'docker' (default) o 'podman'.")] string? containerRuntime,
        [Description("Si true, el script intentará instalar el runtime con apt-get/yum.")] bool? installContainerRuntime,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsRead);
        }
        var runtime = string.IsNullOrWhiteSpace(containerRuntime) ? "docker" : containerRuntime!;
        var result = await mediator.Send(
            new GetInstallScriptQuery(vmId, runtime, installContainerRuntime ?? false), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: result.Value,
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_get_install_status",
                    Why: "Después de pegar el script en la VM, esperá 30-60s y poll el status.",
                    SuggestedArgs: new { vm_id = vmId }),
            ]);
    }

    [McpServerTool(Name = "aethra_reinstall_satellite", Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Re-instala el satélite en una VM usando las credenciales SSH guardadas (cifradas) en el central. " +
        "Falla si no hay credenciales (en cuyo caso usá aethra_install_satellite).")]
    public async Task<object> ReinstallSatelliteAsync(
        [Description("ID de la VM (formato 'vm_...').")] string vmId,
        [Description("Container runtime: 'docker' (default) o 'podman'.")] string? containerRuntime,
        [Description("Si true, instala docker/podman si falta.")] bool? installContainerRuntime,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsWrite);
        }
        var runtime = string.IsNullOrWhiteSpace(containerRuntime) ? "docker" : containerRuntime!;
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"POST /api/vms/{vmId}/install/reinstall",
                plan: new
                {
                    vm_id = vmId,
                    using_saved_credentials = true,
                    container_runtime = runtime,
                    install_container_runtime = installContainerRuntime ?? false,
                });
        }
        var result = await mediator.Send(
            new ReinstallSatelliteCommand(vmId, installContainerRuntime ?? false, runtime), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: new
            {
                vm_id = vmId,
                status = result.Value.Status,
                install_url = result.Value.InstallUrl,
            },
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_get_install_status",
                    Why: "Poll cada ~5s hasta status='Installed' o 'Failed'.",
                    SuggestedArgs: new { vm_id = vmId }),
            ]);
    }

    private static SshCredentialsInput MapSsh(SshInput ssh) => new(
        Host: ssh.Host,
        Port: ssh.Port <= 0 ? 22 : ssh.Port,
        User: ssh.User,
        AuthMethod: ssh.AuthMethod,
        Value: ssh.KeyOrPassword);
}
