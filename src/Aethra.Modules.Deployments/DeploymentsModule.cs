using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Infrastructure.Build;
using Aethra.Modules.Deployments.Infrastructure.Deployment;
using Aethra.Modules.Deployments.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aethra.Modules.Deployments;

/// <summary>
/// Punto de entrada del módulo Deployments.
///
/// F9.3 entrega el pipeline completo en modo dry-run hasta F9.3.5/F9.4:
/// <list type="bullet">
///   <item><b>Build</b> (A7): cola in-process + orquestador + worker, endpoints <c>/api/builds</c>
///         y webhook <c>/webhooks/git</c>.</item>
///   <item><b>Deployment</b> (A8): cola in-process + orquestador + worker, endpoints
///         <c>/api/deployments</c>. El subscriber <c>BuildCompletedHandler</c> hace fan-out
///         a N Deployments cuando un Build completa (MediatR autoscan del ensamblado).</item>
/// </list>
///
/// El atomic swap YARP se materializa via <c>DeploymentCompletedIntegrationEvent</c> al outbox;
/// el módulo Proxy consume el evento y actualiza la Route en caliente.
/// </summary>
public static class DeploymentsModule
{
    public static IServiceCollection AddDeploymentsModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<DeploymentsDbContext>(conn);

        // A7 — Build pipeline: cola in-process + orquestador scoped + worker BackgroundService.
        services.AddSingleton<IBuildJobQueue, InMemoryBuildJobQueue>();
        services.AddScoped<IBuildOrchestrator, BuildOrchestrator>();
        services.AddHostedService<BuildWorker>();

        // F10.1: builder del contexto de build (clone Git real + tar.gz). Singleton — sin estado,
        // crea/limpia su propio directorio temporal por invocación.
        services.AddSingleton<IBuildContextBuilder, BuildContextBuilder>();

        // A8 — Deployment pipeline: cola in-process + orquestador scoped + worker BackgroundService.
        // El BuildCompletedHandler se registra automáticamente vía MediatR autoscan en Program.cs
        // (scanea todos los assemblies de Aethra.Modules.*).
        services.AddSingleton<IDeploymentJobQueue, InMemoryDeploymentJobQueue>();
        services.AddScoped<IDeploymentOrchestrator, DeploymentOrchestrator>();
        services.AddHostedService<DeploymentWorker>();

        return services;
    }

    public static IEndpointRouteBuilder MapDeploymentsModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapBuildEndpoints();
        app.MapWebhookEndpoints();
        app.MapDeploymentEndpoints();
        return app;
    }
}
