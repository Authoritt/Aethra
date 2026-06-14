using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Modules.Metrics.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Metrics;

public static class MetricsModule
{
    public static IServiceCollection AddMetricsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");
        services.AddAethraModuleDbContext<MetricsDbContext>(conn);

        // Retención de métricas crudas (evita el crecimiento ilimitado del disco) — sección "Metrics".
        services.Configure<MetricsRetentionOptions>(configuration.GetSection("Metrics"));
        services.AddHostedService<MetricsRetentionWorker>();
        return services;
    }

    public static IEndpointRouteBuilder MapMetricsModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapMetricsEndpoints();
}
