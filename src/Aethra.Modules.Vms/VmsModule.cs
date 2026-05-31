using Aethra.Modules.Vms.Infrastructure;
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
/// </summary>
public static class VmsModule
{
    public static IServiceCollection AddVmsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");
        services.AddAethraModuleDbContext<VmsDbContext>(conn);
        services.AddScoped<Authentication.ISatelliteAuthenticator, Authentication.DbSatelliteAuthenticator>();
        return services;
    }

    public static IEndpointRouteBuilder MapVmsModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapVmsEndpoints();
        app.MapSatelliteHub();
        return app;
    }
}
