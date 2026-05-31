using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Aethra.Modules.Proxy.Infrastructure.Yarp;

/// <summary>
/// Adapter BD → YARP. Lee <c>routes</c> de Postgres y construye <see cref="IProxyConfig"/>.
/// Cuando el módulo cambia la BD (create/delete route) llama <see cref="ReloadAsync"/> y
/// YARP recoge la nueva configuración en caliente sin restart.
/// </summary>
public sealed class DatabaseProxyConfigProvider : IProxyConfigProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;
    private volatile InMemoryConfig _config;

    public DatabaseProxyConfigProvider(IServiceScopeFactory scopeFactory, ILogger<DatabaseProxyConfigProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = LoadFromDatabase();
    }

    public IProxyConfig GetConfig() => _config;

    /// <summary>
    /// Recarga la config desde la BD y dispara el reload-token de YARP.
    /// Llamado por <c>IProxyConfigService</c> tras cada cambio.
    /// </summary>
    public void Reload()
    {
        var old = _config;
        _config = LoadFromDatabase();
        old.SignalChange();
        _logger.LogInformation("YARP config recargada: {Routes} rutas, {Clusters} clusters",
            _config.Routes.Count, _config.Clusters.Count);
    }

    private InMemoryConfig LoadFromDatabase()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        var routes = db.Routes.AsNoTracking().ToList();

        var yarpRoutes = new List<RouteConfig>(routes.Count);
        var yarpClusters = new List<ClusterConfig>(routes.Count);

        foreach (var r in routes)
        {
            var clusterId = $"cluster:{r.Id}";
            yarpRoutes.Add(new RouteConfig
            {
                RouteId = r.Id.ToString(),
                ClusterId = clusterId,
                Match = new RouteMatch
                {
                    Hosts = [r.Hostname.Value],
                    Path = "/{**catch-all}",
                },
            });
            yarpClusters.Add(new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
                {
                    ["primary"] = new DestinationConfig { Address = r.BackendUrl },
                },
            });
        }

        return new InMemoryConfig(yarpRoutes, yarpClusters);
    }

    private sealed class InMemoryConfig : IProxyConfig, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public InMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }

        internal void SignalChange()
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { /* ya disparado */ }
        }

        public void Dispose() => _cts.Dispose();
    }
}
