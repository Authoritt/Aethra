using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Deployments;

/// <summary>
/// Punto de entrada del módulo Deployments.
///
/// Estado F9.0 cleanup: el módulo está en stub. Solo registra el DbContext (vacío de DbSets,
/// solo con outbox) para que MigrationsBootstrap no falle por ausencia y para reservar el
/// schema en BD. Las dependencias DI (DeployOrchestrator, DeployWorker, IDeployJobQueue) se
/// reintroducirán en F9.3/F9.4 sobre el nuevo modelo Build + DeployTask.
///
/// El handler de webhooks vive aquí (<c>Presentation.WebhookEndpoints</c>) pero está stubeado
/// — devuelve 503 hasta que F9.3 cablee el nuevo lookup ITemplateLookup.
/// </summary>
public static class DeploymentsModule
{
    public static IServiceCollection AddDeploymentsModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<DeploymentsDbContext>(conn);

        return services;
    }

    /// <summary>
    /// Stub. F9.3 reintroducirá <c>MapWebhookEndpoints</c> y <c>MapDeploysEndpoints</c>
    /// con la nueva surface API basada en Templates + Instances.
    /// </summary>
    public static IEndpointRouteBuilder MapDeploymentsModuleEndpoints(this IEndpointRouteBuilder app)
        => app;
}
