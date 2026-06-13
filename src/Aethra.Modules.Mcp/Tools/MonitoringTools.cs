using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Monitoring.UseCases.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class MonitoringTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_monitors", ReadOnly = true, OpenWorld = false)]
    [Description("Lista monitores HTTP con filtros opcionales (instance_id, project_id, status, is_enabled).")]
    public async Task<object> ListAsync(
        [Description("Filtro opcional por instance_id.")] string? instanceId,
        [Description("Filtro opcional por project_id.")] string? projectId,
        [Description("Filtro opcional por status ('Up', 'Down', 'Degraded', 'Unknown').")] string? status,
        [Description("Filtro opcional por is_enabled.")] bool? isEnabled,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringRead))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringRead);
        }
        var q = new ListMonitorsQuery(instanceId, projectId, status, isEnabled);
        var result = await mediator.Send(q, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_monitor_status", ReadOnly = true, OpenWorld = false)]
    [Description("Counts agregados de monitores por estado (up/down/degraded/unknown/disabled). Una sola query a BD.")]
    public async Task<object> SummaryAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringRead))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringRead);
        }
        var result = await mediator.Send(new GetMonitorSummaryQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_monitor", ReadOnly = true, OpenWorld = false)]
    [Description("Detalle de un monitor HTTP: url, método, status codes esperados, intervalo, timeout, estado, "
        + "habilitado, fallos consecutivos, último chequeo y timestamps. Read-only. Por seguridad NO devuelve los "
        + "headers ni el body template del request (pueden traer tokens) — sólo indica si existen vía "
        + "has_custom_headers / has_body_template.")]
    public async Task<object> GetAsync(
        [Description("ID del monitor (formato 'mon_...').")] string monitorId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringRead))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringRead);
        }
        var result = await mediator.Send(new GetMonitorByIdQuery(monitorId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        var m = result.Value;
        return McpResponses.Ok(new
        {
            id = m.Id,
            slug = m.Slug,
            name = m.Name,
            url = m.Url,
            http_method = m.HttpMethod,
            expected_status_codes = m.ExpectedStatusCodes,
            interval_sec = m.IntervalSec,
            timeout_ms = m.TimeoutMs,
            instance_id = m.InstanceId,
            project_id = m.ProjectId,
            is_enabled = m.IsEnabled,
            status = m.Status,
            last_checked_at = m.LastCheckedAt,
            consecutive_failures = m.ConsecutiveFailures,
            created_at = m.CreatedAt,
            updated_at = m.UpdatedAt,
            has_custom_headers = m.Headers is { Count: > 0 },
            has_body_template = !string.IsNullOrEmpty(m.BodyTemplate),
        });
    }

    [McpServerTool(Name = "aethra_enable_monitor", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Activa un monitor HTTP: vuelve a programarse y a chequear el endpoint en su intervalo. "
        + "Idempotente (activar uno ya activo es no-op). Confirma el estado; NO devuelve headers ni body del check.")]
    public async Task<object> EnableAsync(
        [Description("ID del monitor (formato 'mon_...').")] string monitorId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringWrite);
        }
        var result = await mediator.Send(new EnableMonitorCommand(monitorId), ct).ConfigureAwait(false);
        return result.IsSuccess ? OkMonitorState(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_disable_monitor", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Desactiva un monitor HTTP: deja de chequear el endpoint y de emitir alertas (queda en disabled). "
        + "Idempotente. Útil para silenciar durante mantenimientos. Confirma el estado; NO devuelve headers ni body.")]
    public async Task<object> DisableAsync(
        [Description("ID del monitor (formato 'mon_...').")] string monitorId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringWrite);
        }
        var result = await mediator.Send(new DisableMonitorCommand(monitorId), ct).ConfigureAwait(false);
        return result.IsSuccess ? OkMonitorState(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_trigger_monitor_check", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Fuerza un chequeo on-demand de un monitor HTTP AHORA (sin esperar al intervalo): hace la request al "
        + "endpoint, registra el resultado y puede actualizar el status del monitor. Devuelve el check: status, "
        + "http_status_code, latency_ms, error y un snippet de la respuesta. Útil para revalidar tras un fix.")]
    public async Task<object> TriggerCheckAsync(
        [Description("ID del monitor a chequear (formato 'mon_...').")] string monitorId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringWrite);
        }
        var result = await mediator.Send(new TriggerMonitorCheckCommand(monitorId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_monitor", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Elimina permanentemente un monitor HTTP y su historial de checks. Útil para quitar monitores "
        + "obsoletos o falsos-positivos (ej. hostnames que ya no existen). Para silenciar temporalmente sin perder "
        + "el histórico, preferí aethra_disable_monitor.")]
    public async Task<object> DeleteAsync(
        [Description("ID del monitor a eliminar (formato 'mon_...').")] string monitorId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringWrite);
        }
        var result = await mediator.Send(new DeleteMonitorCommand(monitorId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { monitor_id = monitorId, deleted = true })
            : McpResponses.FromError(result.Error);
    }

    // Proyección segura del detalle: confirma el toggle sin filtrar Headers/BodyTemplate (pueden traer tokens de auth).
    private static object OkMonitorState(Aethra.Modules.Monitoring.UseCases.Dtos.MonitorDetailDto m)
        => McpResponses.Ok(new
        {
            monitor_id = m.Id,
            slug = m.Slug,
            name = m.Name,
            is_enabled = m.IsEnabled,
            status = m.Status,
        });
}
