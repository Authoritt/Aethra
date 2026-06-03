using System.Collections.Concurrent;
using Aethra.Modules.Services.Domain;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Scheduling;

/// <summary>
/// F12.1A — BackgroundService que dispara los <see cref="ScheduledJob"/> cuando llega su
/// proximo tick. Loop cada 30s, calcula proximo tick con <see cref="CronExpression"/>, lanza
/// el comando via <see cref="ISatelliteRpcClient.SendExecAsync"/> contra el contenedor del
/// servicio asociado.
///
/// Concurrencia: respeta <see cref="ScheduledJob.MaxConcurrent"/>. Mantiene en memoria un
/// contador de runs activos por job — si se queda corto, el run se saltea (no se encola).
/// </summary>
public sealed class ScheduledJobWorker(
    IServiceScopeFactory scopeFactory,
    ISatelliteRpcClient rpc,
    IClock clock,
    ILogger<ScheduledJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    // Counter por jobId de runs en curso. Se actualiza en el mismo proceso; multi-host central
    // requeriria un store distribuido (Redis), pero F0..F12 asume single-host.
    private readonly ConcurrentDictionary<string, int> _activeRuns = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ScheduledJobWorker arrancando (poll cada {Seconds}s)", PollInterval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ScheduledJobWorker: fallo loop");
            }
            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();

        var now = clock.UtcNow;
        var jobs = await db.ScheduledJobs.AsNoTracking()
            .Where(j => j.Enabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var job in jobs)
        {
            try
            {
                await TickJobAsync(job, now, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ScheduledJobWorker: error procesando job {JobId}", job.Id);
            }
        }
    }

    private async Task TickJobAsync(ScheduledJob job, DateTimeOffset now, CancellationToken ct)
    {
        if (!CronExpression.TryParse(job.CronExpression, out var cron) || cron is null)
        {
            logger.LogWarning("Job {JobId} ({Name}) tiene cron invalido '{Cron}', saltando",
                job.Id, job.Name, job.CronExpression);
            return;
        }

        var tz = ResolveTimeZone(job.TimeZone);

        // Si no hay NextRunAt calculado todavia, calcularlo desde "ahora" hacia adelante.
        // Si existe y aun no se cumplio, no hacer nada en este tick.
        if (job.NextRunAt is { } nextRun && nextRun > now)
        {
            return;
        }

        var fireTime = job.NextRunAt ?? cron.GetNextOccurrence(now.AddMinutes(-1), tz);
        if (fireTime is null || fireTime.Value > now)
        {
            // Calculamos el proximo tick y persistimos. Solo si difiere.
            var next = cron.GetNextOccurrence(now, tz);
            if (next is { } n && (job.NextRunAt is null || job.NextRunAt.Value != n))
            {
                await PersistNextRunAtAsync(job.Id, n, ct).ConfigureAwait(false);
            }
            return;
        }

        // Es momento de disparar. Concurrencia.
        var key = job.Id.ToString();
        var active = _activeRuns.GetOrAdd(key, 0);
        if (active >= job.MaxConcurrent)
        {
            logger.LogInformation("Job {JobId} skip: ya hay {Active} runs activos (max={Max})",
                job.Id, active, job.MaxConcurrent);
            // Recalcular el proximo tick desde "ahora" para evitar quedarnos atascados.
            var next = cron.GetNextOccurrence(now, tz);
            await PersistNextRunAtAsync(job.Id, next, ct).ConfigureAwait(false);
            return;
        }
        _activeRuns.AddOrUpdate(key, 1, (_, v) => v + 1);

        // Persistimos LastRunAt/NextRunAt antes de ejecutar.
        var nextAfter = cron.GetNextOccurrence(now, tz);
        await PersistRunStartedAsync(job.Id, now, nextAfter, ct).ConfigureAwait(false);

        // Lanzamos en fire-and-forget: el RPC al satellite puede tardar (timeoutSeconds).
        // No queremos bloquear el tick del worker.
        _ = ExecuteJobAsync(job.Id, ct);
    }

    private async Task ExecuteJobAsync(ScheduledJobId jobId, CancellationToken ct)
    {
        var key = jobId.ToString();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();

            var job = await db.ScheduledJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, ct)
                .ConfigureAwait(false);
            if (job is null)
            {
                logger.LogWarning("Job {JobId} desaparecio antes de ejecutarse", jobId);
                return;
            }

            var svc = await db.ManagedServices.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == job.ServiceId, ct)
                .ConfigureAwait(false);
            if (svc is null)
            {
                logger.LogWarning("Job {JobId} apunta a servicio inexistente {SvcId}", jobId, job.ServiceId);
                return;
            }

            // Creamos el run row antes de invocar el RPC.
            var run = ScheduledJobRun.Start(jobId, clock.UtcNow);
            db.ScheduledJobRuns.Add(run);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            try
            {
                var result = await rpc.SendExecAsync(
                    svc.TargetVmId, svc.ContainerName, job.Command, job.TimeoutSeconds, ct)
                    .ConfigureAwait(false);

                if (result.TimedOut)
                {
                    run.MarkTimedOut(result.Stdout, result.Stderr, clock.UtcNow);
                }
                else
                {
                    run.MarkCompleted(result.ExitCode, result.Stdout, result.Stderr, clock.UtcNow);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                run.MarkCancelled(clock.UtcNow);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobId} fallo en exec", jobId);
                run.MarkFailed($"exec.failed: {ex.GetType().Name}: {ex.Message}", clock.UtcNow);
            }

            db.ScheduledJobRuns.Update(run);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteJobAsync para {JobId} reviento", jobId);
        }
        finally
        {
            _activeRuns.AddOrUpdate(key, 0, (_, v) => Math.Max(0, v - 1));
        }
    }

    private async Task PersistNextRunAtAsync(ScheduledJobId jobId, DateTimeOffset? next, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();
        var job = await db.ScheduledJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            .ConfigureAwait(false);
        if (job is null) { return; }
        job.SetNextRunAt(next);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task PersistRunStartedAsync(
        ScheduledJobId jobId, DateTimeOffset startedAt, DateTimeOffset? nextRunAt, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();
        var job = await db.ScheduledJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            .ConfigureAwait(false);
        if (job is null) { return; }
        job.MarkRun(startedAt, nextRunAt);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Trigger manual (RunNow). Crea un run inmediato sin tocar NextRunAt.</summary>
    public async Task<ScheduledJobRunId?> TriggerNowAsync(ScheduledJobId jobId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();
        var job = await db.ScheduledJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            .ConfigureAwait(false);
        if (job is null) { return null; }
        var svc = await db.ManagedServices.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == job.ServiceId, ct)
            .ConfigureAwait(false);
        if (svc is null) { return null; }

        var run = ScheduledJobRun.Start(jobId, clock.UtcNow);
        db.ScheduledJobRuns.Add(run);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _ = ExecuteRunInBackgroundAsync(svc.TargetVmId, svc.ContainerName, job.Command,
            job.TimeoutSeconds, run.Id);
        return run.Id;
    }

    private async Task ExecuteRunInBackgroundAsync(
        string vmId, string containerName, string command, int timeoutSeconds, ScheduledJobRunId runId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();
        var run = await db.ScheduledJobRuns.FirstOrDefaultAsync(r => r.Id == runId).ConfigureAwait(false);
        if (run is null) { return; }
        try
        {
            var result = await rpc.SendExecAsync(vmId, containerName, command, timeoutSeconds, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.TimedOut)
            {
                run.MarkTimedOut(result.Stdout, result.Stderr, clock.UtcNow);
            }
            else
            {
                run.MarkCompleted(result.ExitCode, result.Stdout, result.Stderr, clock.UtcNow);
            }
        }
        catch (Exception ex)
        {
            run.MarkFailed($"exec.failed: {ex.GetType().Name}: {ex.Message}", clock.UtcNow);
        }
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static TimeZoneInfo ResolveTimeZone(string tz)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tz);
        }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}
