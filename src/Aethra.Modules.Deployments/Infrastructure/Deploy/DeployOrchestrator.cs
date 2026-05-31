using Aethra.Modules.Deployments.Domain;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Deploy;

/// <summary>
/// Orquesta el state machine del deploy. Hace cada transición → persiste → publica
/// integration event (DashboardForwarder lo reenvía al frontend en vivo).
///
/// Esta versión NO ejecuta builds reales todavía — F4 entrega el cableado completo y el flujo
/// de estados. La integración con el satélite (BuildImageRequest/RunContainerRequest via SignalR)
/// queda como ejecución condicional: si el orquestador remoto está disponible se llama; si no,
/// el deploy se marca como "skipped_no_executor" y se completa para que el state machine quede
/// trazable. F4.5 hará la integración real cuando Docker esté disponible.
/// </summary>
public sealed class DeployOrchestrator(
    DeploymentsDbContext db,
    IApplicationLookup applicationLookup,
    IEnumerable<IRemoteBuildExecutor> remoteExecutors,
    IOutboxWriter outbox,
    IClock clock,
    ILogger<DeployOrchestrator> logger) : IDeployOrchestrator
{
    // .NET DI no soporta dependencias opcionales (?). Inyectamos IEnumerable<> y tomamos
    // el primero si existe — F4 acepta cero implementaciones (dry-run).
    private readonly IRemoteBuildExecutor? remoteExecutor = remoteExecutors.FirstOrDefault();

    public async Task RunAsync(DeployJobId jobId, CancellationToken ct)
    {
        var job = await db.DeployJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
        {
            logger.LogWarning("DeployJob {Id} no encontrado al ejecutar", jobId);
            return;
        }
        if (job.Status.IsTerminal())
        {
            logger.LogInformation("DeployJob {Id} ya está en estado terminal {Status}; nada que hacer",
                jobId, job.Status);
            return;
        }

        var app = await applicationLookup.GetByIdAsync(job.ApplicationId, ct);
        if (app is null)
        {
            await FailAsync(job, "app_not_found", $"Application '{job.ApplicationId}' no existe", ct);
            return;
        }

        try
        {
            await TransitionAsync(job, DeployStatus.Cloning, ct);
            job.AppendLog(DeployLogLevel.Info, "cloning",
                $"Clonando {app.GitRepoUrl} @ {job.Branch} (sha={job.GitSha[..Math.Min(7, job.GitSha.Length)]})",
                clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await PublishLogsAsync(job, ct);

            // F4: el clone real lo hace el GitCloner (lo wireamos en F4.5 cuando integramos con
            // el ejecutor). Por ahora la transición ocurre simulando éxito si no hay executor.
            if (remoteExecutor is null)
            {
                job.AppendLog(DeployLogLevel.Warn, "cloning",
                    "Modo dry-run: no hay executor de builds disponible (Docker no configurado). " +
                    "El state machine avanza pero NO se construye imagen real.", clock.UtcNow);
            }

            await TransitionAsync(job, DeployStatus.Building, ct);
            var imageTag = $"{app.Slug}:{job.GitSha[..Math.Min(7, job.GitSha.Length)]}";

            if (remoteExecutor is not null)
            {
                var buildResult = await remoteExecutor.BuildAsync(app, job, imageTag, ct);
                if (!buildResult.Success)
                {
                    await FailAsync(job, "build_failed", buildResult.ErrorMessage ?? "Build falló", ct);
                    return;
                }
            }
            job.RecordBuildResult(imageTag, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await PublishLogsAsync(job, ct);

            await TransitionAsync(job, DeployStatus.Healthcheck, ct);
            // F4.5: ejecutar healthcheck real del contenedor recién creado antes de swap.

            await TransitionAsync(job, DeployStatus.Swapping, ct);
            if (remoteExecutor is not null && app.PrimaryContainerPort is { } port)
            {
                var runResult = await remoteExecutor.RunAsync(app, job, imageTag, ct);
                if (!runResult.Success)
                {
                    await FailAsync(job, "run_failed", runResult.ErrorMessage ?? "Run falló", ct);
                    return;
                }
                job.RecordRunResult(app.ContainerName, port, clock.UtcNow);
                await db.SaveChangesAsync(ct);
            }

            job.Complete(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await PublishLogsAsync(job, ct);
            await outbox.EnqueueAsync(
                new DeployStatusChangedEvent(job.Id.ToString(), job.ApplicationId,
                    DeployStatus.Swapping.ToString(), DeployStatus.Completed.ToString(), clock.UtcNow),
                ct);
        }
        catch (Exception ex)
        {
            await FailAsync(job, "unhandled", ex.Message, ct);
            logger.LogError(ex, "DeployJob {Id} falló inesperadamente", jobId);
        }
    }

    private async Task TransitionAsync(DeployJob job, DeployStatus to, CancellationToken ct)
    {
        var from = job.Status;
        job.Transition(to, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await outbox.EnqueueAsync(
            new DeployStatusChangedEvent(job.Id.ToString(), job.ApplicationId,
                from.ToString(), to.ToString(), clock.UtcNow),
            ct);
    }

    private async Task FailAsync(DeployJob job, string code, string message, CancellationToken ct)
    {
        job.Fail(code, message, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await outbox.EnqueueAsync(
            new DeployStatusChangedEvent(job.Id.ToString(), job.ApplicationId,
                job.FailedAtStage!.Value.ToString(), DeployStatus.Failed.ToString(), clock.UtcNow),
            ct);
        await PublishLogsAsync(job, ct);
    }

    private async Task PublishLogsAsync(DeployJob job, CancellationToken ct)
    {
        // Solo publicamos los últimos logs no publicados. Simplificación: publicamos todos los
        // del último step. F5 podría agregar un "PublishedSequence" en BD para tracking exacto.
        var recent = job.Logs.TakeLast(5);
        foreach (var l in recent)
        {
            await outbox.EnqueueAsync(
                new DeployLogAppendedEvent(job.Id.ToString(), l.Sequence, l.Timestamp,
                    l.Level.ToString(), l.Stage, l.Text),
                ct);
        }
    }
}

/// <summary>
/// Abstracción del ejecutor remoto. Implementaciones (F4.5):
/// - LocalDockerExecutor (Docker.DotNet contra el socket local)
/// - SatelliteRpcExecutor (SignalR central → satélite remoto)
///
/// En F4 puede ser null (modo dry-run para validar wiring sin Docker).
/// </summary>
public interface IRemoteBuildExecutor
{
    Task<BuildOutcome> BuildAsync(ApplicationForDeployView app, DeployJob job, string imageTag, CancellationToken ct);
    Task<RunOutcome> RunAsync(ApplicationForDeployView app, DeployJob job, string imageTag, CancellationToken ct);
}

public sealed record BuildOutcome(bool Success, string? ImageId, string? ErrorMessage);
public sealed record RunOutcome(bool Success, string? ContainerId, string? ErrorMessage);
