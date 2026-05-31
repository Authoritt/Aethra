using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Infrastructure.Deploy;
using Aethra.Modules.Deployments.Infrastructure.Git;
using Aethra.Modules.Deployments.Infrastructure.Queue;
using Aethra.Modules.Deployments.Presentation;
using Aethra.Modules.Deployments.UseCases.Commands;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Deployments;

public static class DeploymentsModule
{
    public static IServiceCollection AddDeploymentsModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<DeploymentsDbContext>(conn);

        // Git clone wrapper (stateless, singleton). Lo usa DeployWorker para checkout antes del build.
        services.AddAethraGit();

        services.AddSingleton<IDeployJobQueue, InMemoryDeployJobQueue>();
        services.AddScoped<IDeployOrchestrator, DeployOrchestrator>();
        services.AddHostedService<DeployWorker>();

        // IRemoteBuildExecutor: opcional en F4. Si nadie registra una implementación,
        // el orquestador entra en modo "dry-run" (state machine completo, sin Docker real).
        // F4.5 registrará LocalDockerExecutor o SatelliteRpcExecutor según target VM.

        return services;
    }

    public static IEndpointRouteBuilder MapDeploymentsModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapWebhookEndpoints();
        app.MapDeploysEndpoints();
        return app;
    }
}
