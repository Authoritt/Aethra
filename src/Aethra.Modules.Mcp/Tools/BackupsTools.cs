using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Services.UseCases.Backups;
using ModelContextProtocol.Server;
using MediatR;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// F11.5 — herramientas para gestionar backups de Managed Services (Postgres/Redis/Rabbit).
/// Trigger on-demand, listar historial, restaurar y borrar.
///
/// <para>
/// IMPORTANTE: <see cref="RestoreServiceAsync"/> y <see cref="DeleteBackupAsync"/> recomendamos
/// llamarlos primero con dry_run=true para ver el plan, y solo entonces re-ejecutar sin dry_run.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class BackupsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_backup_service", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Dispara un backup on-demand del servicio. Crea un ServiceBackup con status Running → Success/Failed. " +
        "Devuelve el backup row con su id y destination_path.")]
    public async Task<object> BackupServiceAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
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
                wouldCall: $"POST /api/services/{serviceId}/backups/run",
                plan: new { serviceId, action = "trigger on-demand backup" });
        }
        var result = await mediator.Send(new RunBackupCommand(serviceId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: result.Value,
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_service_backups",
                    Why: "Mirá el backup recién creado en el listado (status, size_bytes).",
                    SuggestedArgs: new { service_id = serviceId, limit = 10 }),
            ]);
    }

    [McpServerTool(Name = "aethra_list_service_backups", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los backups del servicio (más recientes primero). Read-only.")]
    public async Task<object> ListBackupsAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
        [Description("Cantidad máxima (1..500). Default 50.")] int? limit,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesRead);
        }
        var effective = limit ?? 50;
        var result = await mediator.Send(new ListBackupsQuery(serviceId, effective), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_restore_service", Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("CRITICAL: restaura un backup sobre el servicio actual. Sobreescribe datos. " +
        "Siempre llamá primero con dry_run=true para revisar el plan, después sin dry_run para ejecutar.")]
    public async Task<object> RestoreServiceAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
        [Description("ID del backup a restaurar (formato 'bkp_...').")] string backupId,
        [Description("Si true (recomendado para primera llamada), NO restaura — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"POST /api/services/{serviceId}/backups/{backupId}/restore",
                plan: new
                {
                    serviceId,
                    backupId,
                    impact = "Overwrites current service data with the backup snapshot.",
                    recommendation = "Para ejecutar realmente, re-llamá con dry_run=false.",
                });
        }
        var result = await mediator.Send(new RestoreBackupCommand(serviceId, backupId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: new { service_id = serviceId, backup_id = backupId, restored = true },
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_service_backups",
                    Why: "Confirmá que el restore quedó registrado y verifique el servicio.",
                    SuggestedArgs: new { service_id = serviceId, limit = 5 }),
            ]);
    }

    [McpServerTool(Name = "aethra_delete_backup", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Borra un backup (el archivo físico + el row). Usá dry_run=true primero para confirmar.")]
    public async Task<object> DeleteBackupAsync(
        [Description("ID del Managed Service (formato 'svc_...'). Solo se usa para context — el handler valida que el backup pertenezca al servicio.")] string serviceId,
        [Description("ID del backup a borrar (formato 'bkp_...').")] string backupId,
        [Description("Si true, NO borra — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        _ = serviceId; // Argumento contextual; DeleteBackupCommand sólo necesita backupId.
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"DELETE /api/services/{serviceId}/backups/{backupId}",
                plan: new { serviceId, backupId, action = "permanent delete (file + row)" });
        }
        var result = await mediator.Send(new DeleteBackupCommand(backupId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.Ok(new { backup_id = backupId, deleted = true });
    }
}
