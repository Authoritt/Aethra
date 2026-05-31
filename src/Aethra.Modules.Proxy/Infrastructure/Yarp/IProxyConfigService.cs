namespace Aethra.Modules.Proxy.Infrastructure.Yarp;

/// <summary>
/// Servicio que el resto del módulo usa para señalar que la BD cambió y YARP debe recargar.
/// Centraliza la dependencia con <see cref="DatabaseProxyConfigProvider"/>.
/// </summary>
public interface IProxyConfigService
{
    void Reload();
}

internal sealed class ProxyConfigService(DatabaseProxyConfigProvider provider) : IProxyConfigService
{
    public void Reload() => provider.Reload();
}
