using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Services.UseCases.ScheduledJobs;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// F12.1A — herramientas MCP para gestionar Scheduled Jobs por servicio. Permiten al agente
/// IA crear cron jobs (ej. <c>pg_dump diario</c>, <c>migrations on demand</c>) que se ejecutan
/// dentro del contenedor del Managed Service via <c>docker exec</c>.
/// </summary>
[McpServerToolType]
public sealed class ScheduledJobsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_scheduled_jobs", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los scheduled jobs configurados para un Managed Service (cron schedule + estado).")]
    public async Task<object> ListAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesRead);
        }
        var result = await mediator.Send(new ListScheduledJobsQuery(serviceId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_scheduled_job", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un scheduled job que se ejecuta dentro del contenedor del Managed Service según cron expression. " +
        "Cron formato 5 campos: 'minute hour day month dayOfWeek' (ej. '0 2 * * *' = todos los días a las 02:00).")]
    public async Task<object> CreateAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
        [Description("Nombre human-readable del job.")] string name,
        [Description("Comando a ejecutar dentro del contenedor (sh -c). Ej: 'pg_dump -d myapp > /backup/dump.sql'.")] string command,
        [Description("Cron expression: 'minute hour day month dayOfWeek'. Ejemplos: '0 2 * * *' (diario 02:00), '*/15 * * * *' (cada 15min).")] string cronExpression,
        [Description("Descripción opcional del job.")] string? description,
        [Description("Zona horaria IANA (ej. 'America/Bogota'). Default 'UTC'.")] string? timeZone,
        [Description("Máximo de ejecuciones concurrentes (1+). Default 1.")] int? maxConcurrent,
        [Description("Timeout en segundos. Default 300.")] int? timeoutSeconds,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        var cmd = new CreateScheduledJobCommand(serviceId, name, description, command,
            cronExpression, timeZone, maxConcurrent, timeoutSeconds);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: result.Value,
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_scheduled_jobs",
                    Why: "Confirmar que el job aparece en el listado y verificar next_run_at.",
                    SuggestedArgs: new { service_id = serviceId }),
            ]);
    }

    [McpServerTool(Name = "aethra_trigger_scheduled_job", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Dispara un scheduled job AHORA (out of schedule). Útil para correr migrations on-demand. " +
        "Devuelve run_id que puede consultarse con aethra_list_scheduled_job_runs.")]
    public async Task<object> TriggerAsync(
        [Description("ID del scheduled job (formato 'sch_...').")] string jobId,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"POST /api/scheduled-jobs/{jobId}/run-now",
                plan: new { jobId, action = "trigger immediate run" });
        }
        var result = await mediator.Send(new TriggerScheduledJobCommand(jobId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: new { job_id = jobId, run_id = result.Value },
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_scheduled_job_runs",
                    Why: "Ver stdout/stderr y exit code una vez que termine.",
                    SuggestedArgs: new { job_id = jobId, limit = 5 }),
            ]);
    }

    [McpServerTool(Name = "aethra_list_scheduled_job_runs", ReadOnly = true, OpenWorld = false)]
    [Description("Lista las últimas N ejecuciones de un scheduled job (status, exit code, duration, stdout/stderr truncados a 64KB).")]
    public async Task<object> ListRunsAsync(
        [Description("ID del scheduled job (formato 'sch_...').")] string jobId,
        [Description("Cantidad máxima (1..500). Default 50.")] int? limit,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesRead);
        }
        var effective = limit ?? 50;
        var result = await mediator.Send(new ListScheduledJobRunsQuery(jobId, effective), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
