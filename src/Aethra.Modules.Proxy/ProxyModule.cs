using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure.Tls;
using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Modules.Proxy.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace Aethra.Modules.Proxy;

/// <summary>
/// Punto de entrada del módulo Proxy.
///
/// Wiring en apps/api/Program.cs:
/// 1. services.AddProxyModule(config)
/// 2. services.AddReverseProxy() (de YARP — Aethra.Modules.Proxy registra el IProxyConfigProvider)
/// 3. app.MapProxyModuleEndpoints() para /api/proxy/routes
/// 4. app.MapReverseProxy() AL FINAL del pipeline (catch-all)
/// </summary>
public static class ProxyModule
{
    public static IServiceCollection AddProxyModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<ProxyDbContext>(conn);

        services.AddSingleton<DatabaseProxyConfigProvider>();
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
        services.AddSingleton<IProxyConfigService, ProxyConfigService>();

        // TLS: Certes + Let's Encrypt + HTTP-01 + CertRenewalWorker.
        services.AddAethraTls<ProxyDbContext>(configuration);

        return services;
    }

    public static IEndpointRouteBuilder MapProxyModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapRoutesEndpoints();
        app.MapCertificateEndpoints();
        app.MapAcmeChallengeEndpoint();
        return app;
    }
}
