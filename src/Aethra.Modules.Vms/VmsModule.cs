using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.Infrastructure.Provisioning;
using Aethra.Modules.Vms.Infrastructure.Security;
using Aethra.Modules.Vms.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Vms;

/// <summary>
/// Punto de entrada del módulo Vms. Registra:
/// - DbContext + outbox dispatcher.
/// - <see cref="Authentication.SatelliteTokenAuthHandler"/> para autenticar conexiones SignalR del satélite.
/// - F11.4: codec SSH (DataProtection) + provisioner SSH.NET + cola in-memory + BackgroundService dispatcher.
/// </summary>
public static class VmsModule
{
    public static IServiceCollection AddVmsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");
        services.AddAethraModuleDbContext<VmsDbContext>(conn);
        services.AddScoped<Authentication.ISatelliteAuthenticator, Authentication.DbSatelliteAuthenticator>();

        // F11.4 — Auto-install via SSH.
        services.AddSingleton<ISshCredentialsCodec, DataProtectionSshCredentialsCodec>();
        services.AddSingleton<IInstallationJobQueue, InMemoryInstallationJobQueue>();
        services.AddScoped<ISshProvisioner, RenciSshProvisioner>();
        services.AddHostedService<InstallationDispatcher>();

        return services;
    }

    public static IEndpointRouteBuilder MapVmsModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapVmsEndpoints();
        app.MapSatelliteHub();
        return app;
    }
}
