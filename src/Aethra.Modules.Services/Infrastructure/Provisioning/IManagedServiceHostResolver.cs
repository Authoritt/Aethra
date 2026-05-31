using Aethra.Modules.Services.Domain;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Resuelve el host de red para conectarse a una <see cref="ManagedService"/> desde el
/// proceso que orquesta. En MVP devolvemos el container name (alcanzable por DNS interno
/// de Docker). En despliegues fan-out el resolver puede mapear a IP de la VM o a túnel SSH.
/// </summary>
public interface IManagedServiceHostResolver
{
    Task<string> ResolveAsync(ManagedService service, CancellationToken cancellationToken);

    /// <summary>
    /// Puerto del management/admin endpoint (e.g. RabbitMQ HTTP API en 15672).
    /// </summary>
    Task<int> ResolveManagementPortAsync(ManagedService service, CancellationToken cancellationToken);
}

internal sealed class DirectContainerNameResolver : IManagedServiceHostResolver
{
    public Task<string> ResolveAsync(ManagedService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        return Task.FromResult(service.ContainerName);
    }

    public Task<int> ResolveManagementPortAsync(ManagedService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        return Task.FromResult(service.Type switch
        {
            ServiceType.RabbitMQ => 15672,
            _ => service.InternalPort,
        });
    }
}
