using System.ComponentModel;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Identity.UseCases.Queries;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Monitoring.UseCases.Queries;
using Aethra.Modules.Notifications.UseCases.Queries;
using Aethra.Modules.Services.UseCases.Backups;
using Aethra.Modules.Services.UseCases.Queries;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas de "overview" — el primer call que un agente IA hace para entender qué hay
/// en la instancia de Aethra. Todas son read-only.
///
/// <para>
/// F11.5 — se agregan campos para los nuevos features:
/// <list type="bullet">
///   <item>users + roles (F11.1)</item>
///   <item>notification_channels + recent_failed_deliveries (F11.3A)</item>
///   <item>recent_backups (F11.3B)</item>
///   <item>vms_install_status (F11.4)</item>
/// </list>
/// F9.0 cleanup: la sección "projects/applications" se ha removido temporalmente porque
/// <c>Modules.Projects.UseCases.Projects.Queries</c> dejó de existir. F9.5 reintroducirá un
/// <c>ListProjectsQuery</c> sobre el nuevo modelo Template/Client/Instance.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class ContextTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_context", ReadOnly = true, OpenWorld = false)]
    [Description("Resumen agregado del estado de Aethra: counts de VMs, servicios, dominios, monitores, users, " +
        "notification channels, deliveries fallidos recientes, backups recientes y status de install de cada VM. " +
        "Primer call recomendado para cualquier agente IA.")]
    public async Task<object> ListContextAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ContextRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ContextRead);
        }

        // Lanzamos todas las queries base en paralelo — son AsNoTracking, sin side-effects.
        // Las queries que dependen de los scopes adicionales se ejecutan condicionalmente
        // para que un caller sin esos scopes igualmente reciba el subset que SÍ puede leer.
        var vmsTask = mediator.Send(new ListVmsQuery(), ct);
        var servicesTask = mediator.Send(new ListServicesQuery(), ct);
        var zonesTask = mediator.Send(new ListZonesQuery(), ct);
        var monitorSummaryTask = mediator.Send(new GetMonitorSummaryQuery(), ct);

        var usersTask = caller.HasScope(McpScopes.UsersRead)
            ? mediator.Send(new ListUsersQuery(), ct)
            : Task.FromResult<Aethra.Shared.Kernel.Results.Result<IReadOnlyList<Aethra.Modules.Identity.UseCases.Dtos.UserSummaryDto>>>(
                Array.Empty<Aethra.Modules.Identity.UseCases.Dtos.UserSummaryDto>());

        var rolesTask = caller.HasScope(McpScopes.UsersRead)
            ? mediator.Send(new ListRolesQuery(), ct)
            : Task.FromResult<Aethra.Shared.Kernel.Results.Result<IReadOnlyList<Aethra.Modules.Identity.UseCases.Dtos.RoleDto>>>(
                Array.Empty<Aethra.Modules.Identity.UseCases.Dtos.RoleDto>());

        var channelsTask = caller.HasScope(McpScopes.NotificationsRead)
            ? mediator.Send(new ListChannelsQuery(), ct)
            : Task.FromResult<Aethra.Shared.Kernel.Results.Result<IReadOnlyList<Aethra.Modules.Notifications.UseCases.Dtos.NotificationChannelDto>>>(
                Array.Empty<Aethra.Modules.Notifications.UseCases.Dtos.NotificationChannelDto>());

        var failedDeliveriesTask = caller.HasScope(McpScopes.NotificationsRead)
            ? mediator.Send(new ListDeliveriesQuery(null, "Failed", 50), ct)
            : Task.FromResult<Aethra.Shared.Kernel.Results.Result<IReadOnlyList<Aethra.Modules.Notifications.UseCases.Dtos.NotificationDeliveryDto>>>(
                Array.Empty<Aethra.Modules.Notifications.UseCases.Dtos.NotificationDeliveryDto>());

        await Task.WhenAll(
            vmsTask, servicesTask, zonesTask, monitorSummaryTask,
            usersTask, rolesTask, channelsTask, failedDeliveriesTask).ConfigureAwait(false);

        var vms = vmsTask.Result.IsSuccess ? vmsTask.Result.Value : [];
        var services = servicesTask.Result.IsSuccess ? servicesTask.Result.Value : [];
        var zones = zonesTask.Result.IsSuccess ? zonesTask.Result.Value : [];
        var monitors = monitorSummaryTask.Result.IsSuccess ? monitorSummaryTask.Result.Value : null;
        var users = usersTask.Result.IsSuccess ? usersTask.Result.Value : [];
        var roles = rolesTask.Result.IsSuccess ? rolesTask.Result.Value : [];
        var channels = channelsTask.Result.IsSuccess ? channelsTask.Result.Value : [];
        var failedDeliveries = failedDeliveriesTask.Result.IsSuccess ? failedDeliveriesTask.Result.Value : [];

        // Backups: la ListBackupsQuery requiere service_id, así que iteramos servicios
        // (solo si el caller tiene services:read — los demás no podrían ver bindings tampoco).
        var recentBackups = new List<object>();
        if (caller.HasScope(McpScopes.ServicesRead))
        {
            foreach (var svc in services)
            {
                var bkResult = await mediator.Send(new ListBackupsQuery(svc.Id, 5), ct).ConfigureAwait(false);
                if (!bkResult.IsSuccess) { continue; }
                foreach (var b in bkResult.Value)
                {
                    recentBackups.Add(new
                    {
                        service_id = b.ServiceId,
                        backup_id = b.Id,
                        status = b.Status,
                        size_bytes = b.SizeBytes,
                        started_at = b.StartedAt,
                    });
                }
            }
            // Top 10 más recientes globales (por started_at desc).
            recentBackups = [.. recentBackups
                .OrderByDescending(r => (DateTimeOffset)r.GetType().GetProperty("started_at")!.GetValue(r)!)
                .Take(10)];
        }

        // Install status: leemos cada VM con su status. El ListVmsQuery actual no incluye install_status
        // en el DTO, así que llamamos GetInstallStatusQuery por VM. Es N+1 pero el N es pequeño (max ~3-10 VMs).
        var vmsInstallStatus = new List<object>();
        foreach (var vm in vms)
        {
            var st = await mediator.Send(new GetInstallStatusQuery(vm.Id), ct).ConfigureAwait(false);
            if (!st.IsSuccess) { continue; }
            vmsInstallStatus.Add(new { vm_id = vm.Id, status = st.Value.Status });
        }

        return McpResponses.Ok(new
        {
            counts = new
            {
                vms = vms.Count,
                services = services.Count,
                cloudflare_zones = zones.Count,
                monitors = monitors?.Total ?? 0,
                monitors_up = monitors?.Up ?? 0,
                monitors_down = monitors?.Down ?? 0,
                monitors_degraded = monitors?.Degraded ?? 0,
                users = users.Count,
                roles = roles.Count,
                notification_channels = channels.Count,
                recent_failed_deliveries = failedDeliveries.Count,
            },
            vms = vms.Select(v => new
            {
                id = v.Id,
                slug = v.Slug,
                name = v.Name,
                status = v.Status,
            }),
            services = services.Select(s => new
            {
                id = s.Id,
                slug = s.Slug,
                type = s.Type,
                status = s.Status,
                bindings = s.BindingsCount,
            }),
            cloudflare_zones = zones.Select(z => new
            {
                id = z.Id,
                name = z.Name,
                status = z.Status,
                records = z.RecordsCount,
            }),
            users_count = users.Count,
            users = users.Take(10).Select(u => new
            {
                id = u.Id,
                email = u.Email,
                roles_count = u.Roles.Count,
            }),
            roles = roles.Select(r => new
            {
                slug = r.Slug,
                display_name = r.DisplayName,
                scopes_count = r.Scopes.Count,
            }),
            notification_channels_count = channels.Count,
            recent_failed_deliveries_count = failedDeliveries.Count,
            recent_backups = recentBackups,
            vms_install_status = vmsInstallStatus,
            generated_at = DateTimeOffset.UtcNow,
            projects_pending = "F9.5 reintroducirá Projects/Templates/Clients/Instances aquí.",
        });
    }
}
