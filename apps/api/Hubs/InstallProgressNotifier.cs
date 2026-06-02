using Aethra.Shared.Contracts.Vms;
using Microsoft.AspNetCore.SignalR;

namespace Aethra.Api.Hubs;

/// <summary>
/// Implementación de <see cref="IInstallProgressNotifier"/> sobre <see cref="DashboardHub"/>.
/// El frontend que abrió el form de auto-install se suscribe al grupo <c>vm:{vmId}</c> via
/// <c>JoinVm</c> y escucha los eventos <c>VmInstallLog</c> + <c>VmInstallStatusChanged</c>.
/// </summary>
public sealed class InstallProgressNotifier(IHubContext<DashboardHub> hub) : IInstallProgressNotifier
{
    public Task PublishLogAsync(string vmId, string line, string level = "info",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmId);
        ArgumentNullException.ThrowIfNull(line);
        var payload = new { vmId, line, level, timestamp = DateTimeOffset.UtcNow };
        return hub.Clients.Group(DashboardHub.VmGroup(vmId))
            .SendAsync("VmInstallLog", payload, cancellationToken);
    }

    public Task PublishStatusAsync(string vmId, string status, string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        var payload = new { vmId, status, errorCode, timestamp = DateTimeOffset.UtcNow };
        return hub.Clients.Group(DashboardHub.VmGroup(vmId))
            .SendAsync("VmInstallStatusChanged", payload, cancellationToken);
    }
}
