using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting;

namespace Aethra.Modules.Vms.Presentation;

public static class VmsEndpoints
{
    private const string ScopeRead = "scope:vms:read";
    private const string ScopeWrite = "scope:vms:write";

    public static IEndpointRouteBuilder MapVmsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vms").WithTags("Vms");

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            ToResult(await mediator.Send(new ListVmsQuery(), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListVms");

        group.MapGet("/{vmId}", async (string vmId, IMediator mediator, CancellationToken ct) =>
            ToResult(await mediator.Send(new GetVmByIdQuery(vmId), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetVm");

        group.MapPost("/", async ([FromBody] RegisterVmRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var cmd = new RegisterVmCommand(body.Name, body.Slug, body.PublicIp, body.PrivateIp, body.Description);
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess ? Results.Created($"/api/vms/{r.Value.VmId}", r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RegisterVm")
        .WithDescription("Registra una VM. La respuesta incluye el TOKEN UNA SOLA VEZ.");

        // F11.4 — Auto-install via SSH.
        group.MapPost("/{vmId}/install/auto", async (
                string vmId,
                [FromBody] AutoInstallRequest body,
                IMediator mediator,
                CancellationToken ct) =>
        {
            var creds = body.Ssh is null
                ? null
                : new SshCredentialsInput(
                    Host: body.Ssh.Host,
                    Port: body.Ssh.Port,
                    User: body.Ssh.User,
                    AuthMethod: body.Ssh.AuthMethod,
                    Value: body.Ssh.Value);
            var cmd = new AutoInstallSatelliteCommand(
                VmId: vmId,
                Credentials: creds,
                InstallContainerRuntime: body.InstallContainerRuntime,
                ContainerRuntime: body.ContainerRuntime ?? "docker",
                DryRun: body.DryRun);
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess ? Results.Accepted(r.Value.InstallUrl, r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("AutoInstallSatellite")
        .WithDescription("Auto-instala el satélite vía SSH desde el central.");

        // F11.4 — Estado de instalación.
        group.MapGet("/{vmId}/install/status", async (string vmId, IMediator mediator, CancellationToken ct) =>
            ToResult(await mediator.Send(new GetInstallStatusQuery(vmId), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetInstallStatus");

        // F11.4 — Reinstall usando credenciales guardadas.
        group.MapPost("/{vmId}/install/reinstall", async (
                string vmId,
                [FromBody] ReinstallRequest? body,
                IMediator mediator,
                CancellationToken ct) =>
        {
            var cmd = new ReinstallSatelliteCommand(
                VmId: vmId,
                InstallContainerRuntime: body?.InstallContainerRuntime ?? false,
                ContainerRuntime: body?.ContainerRuntime ?? "docker");
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess ? Results.Accepted(r.Value.InstallUrl, r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("ReinstallSatellite");

        // F11.4 — Script bash one-liner. Rota el token cada vez.
        group.MapGet("/{vmId}/install-script", async (
                string vmId,
                [FromQuery(Name = "container_runtime")] string? containerRuntime,
                [FromQuery(Name = "install_container_runtime")] bool installContainerRuntime,
                IMediator mediator,
                CancellationToken ct) =>
        {
            var q = new GetInstallScriptQuery(vmId, containerRuntime ?? "docker", installContainerRuntime);
            return ToResult(await mediator.Send(q, ct));
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("GetInstallScript");

        // F11.4 — Endpoint público que sirve el tarball del satélite. Sin auth porque el
        // script bash necesita descargar sin headers. La SEGURIDAD viene del token Bearer
        // que el satélite usa para el handshake (validado por SatelliteTokenAuthHandler).
        app.MapGet("/api/satellite/binary", (
                [FromQuery] string arch,
                IHostEnvironment env) =>
        {
            if (string.IsNullOrWhiteSpace(arch))
            {
                return Results.BadRequest(new { error = "missing_arch", message = "Query param 'arch' requerido (linux-x64 o linux-arm64)." });
            }
            if (arch is not "linux-x64" and not "linux-arm64")
            {
                return Results.BadRequest(new { error = "unsupported_arch", message = $"Arch no soportada: {arch}." });
            }
            var basePath = Path.Combine(env.ContentRootPath, "satellite-binaries");
            var file = Path.Combine(basePath, $"aethra-satellite-{arch}.tar.gz");
            if (!File.Exists(file))
            {
                return Results.Json(new
                {
                    error = "binary_not_published",
                    message = "El binario del satélite no está disponible. Corre `scripts/publish-satellite.sh` primero.",
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            var contentType = "application/gzip";
            return Results.File(file, contentType, $"aethra-satellite-{arch}.tar.gz");
        })
        .AllowAnonymous()
        .WithName("DownloadSatelliteBinary")
        .WithTags("Vms");

        // F11.4 — Endpoint público que sirve el script bash de install. Permite el flow
        // `curl -fsSL .../install-satellite.sh | sudo bash -s -- ...`.
        app.MapGet("/install-satellite.sh", (IHostEnvironment env) =>
        {
            var scriptPath = ResolveInstallScriptPath(env);
            if (!File.Exists(scriptPath))
            {
                return Results.Json(new
                {
                    error = "script_not_found",
                    message = "scripts/install-satellite.sh no está disponible en el servidor.",
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            // Servimos como text/x-shellscript para que curl no se queje, pero text/plain
            // también funciona (curl -fsSL no valida content-type).
            return Results.File(scriptPath, "text/x-shellscript", "install-satellite.sh");
        })
        .AllowAnonymous()
        .WithName("DownloadInstallScript");

        // F12.3 — opt-in / opt-out al pool de previews.
        group.MapPatch("/{vmId}/accepts-previews", async (
            string vmId,
            [FromBody] SetAcceptsPreviewsRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new SetAcceptsPreviewsCommand(vmId, body.AcceptsPreviews), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("SetVmAcceptsPreviews");

        return app;
    }

    private static string ResolveInstallScriptPath(IHostEnvironment env)
    {
        // En desarrollo el script vive en el repo bajo scripts/. En despliegues custom,
        // el operador puede copiarlo al ContentRoot/satellite-binaries/ — chequeamos ambos.
        var inRepo = Path.Combine(env.ContentRootPath, "..", "..", "scripts", "install-satellite.sh");
        if (File.Exists(inRepo))
        {
            return Path.GetFullPath(inRepo);
        }
        var inContentRoot = Path.Combine(env.ContentRootPath, "satellite-binaries", "install-satellite.sh");
        return inContentRoot;
    }

    public sealed record RegisterVmRequest(string Name, string? Slug, string? PublicIp, string? PrivateIp,
        string? Description);

    public sealed record AutoInstallRequest(
        SshBlock? Ssh,
        bool InstallContainerRuntime,
        string? ContainerRuntime,
        bool DryRun);

    public sealed record SshBlock(string Host, int Port, string User, string AuthMethod, string Value);

    public sealed record ReinstallRequest(bool InstallContainerRuntime, string? ContainerRuntime);

    /// <summary>F12.3 — body para <c>PATCH /api/vms/{id}/accepts-previews</c>.</summary>
    public sealed record SetAcceptsPreviewsRequest(bool AcceptsPreviews);

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };
}
