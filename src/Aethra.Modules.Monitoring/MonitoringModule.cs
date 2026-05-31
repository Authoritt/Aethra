using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.Infrastructure.Probes;
using Aethra.Modules.Monitoring.Infrastructure.Worker;
using Aethra.Modules.Monitoring.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Monitoring;

/// <summary>
/// Punto de entrada del módulo Monitoring.
///
/// Wiring en apps/api/Program.cs:
/// <code>
///   services.AddMonitoringModule(config);
///   app.MapMonitoringModuleEndpoints();
/// </code>
///
/// El módulo agrega:
/// <list type="bullet">
///   <item><see cref="MonitoringDbContext"/> con schema <c>monitoring</c>.</item>
///   <item><see cref="IMonitorProbe"/> + <see cref="HttpMonitorProbe"/> con HttpClient nombrado.</item>
///   <item><see cref="MonitorWorker"/> como BackgroundService que ejecuta probes en intervalos.</item>
///   <item>Endpoints REST bajo <c>/api/monitors</c>.</item>
/// </list>
/// </summary>
public static class MonitoringModule
{
    public static IServiceCollection AddMonitoringModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<MonitoringDbContext>(conn);

        // HttpClient con timeout "infinito" — el probe enforce el suyo por monitor con CTS.
        // Importante: si dejamos el default (100s) un timeout corto del monitor no se respeta:
        // HttpClient cancela primero. Por eso lo desactivamos a nivel cliente y delegamos en el probe.
        services.AddHttpClient(HttpMonitorProbe.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Aethra-Monitor/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // No queremos seguir redirects ciegamente: una 302 a otra URL puede falsear el uptime.
            AllowAutoRedirect = false,
        });

        services.AddScoped<IMonitorProbe, HttpMonitorProbe>();
        services.AddHostedService<MonitorWorker>();

        return services;
    }

    public static IEndpointRouteBuilder MapMonitoringModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapMonitorsEndpoints();
}
