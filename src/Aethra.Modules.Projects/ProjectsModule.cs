using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Projects;

/// <summary>
/// Punto de entrada del módulo Projects.
///
/// Estado F9.0 cleanup: registra el DbContext (vacío de DbSets) y NoOp lookups/writers para
/// que los módulos consumidores (Services, Mcp, Cloudflare, Monitoring) puedan resolver sus
/// dependencias a través de Shared.Contracts mientras A1 completa los aggregates nuevos y
/// la sub-fase persistence cablea las impls EF reales.
///
/// Endpoints REST no se mapean en esta fase — se reescribirán en F9.5 sobre los nuevos
/// commands/queries (Template, Client, Instance).
/// </summary>
public static class ProjectsModule
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<ProjectsDbContext>(conn);

        // Read-models y writers cross-module (Shared.Contracts) — stubs NoOp que la sub-fase
        // persistence sustituirá por implementaciones EF reales.
        services.AddScoped<ITemplateLookup, NoOpTemplateLookup>();
        services.AddScoped<IInstanceLookup, NoOpInstanceLookup>();
        services.AddScoped<ITenantContext, NoOpTenantContext>();
        services.AddScoped<IEnvVarWriter, NoOpEnvVarWriter>();
        services.AddScoped<ISecretWriter, NoOpSecretWriter>();

        return services;
    }

    /// <summary>
    /// Stub. F9.5 reintroducirá endpoints REST sobre los nuevos commands/queries.
    /// </summary>
    public static IEndpointRouteBuilder MapProjectsModuleEndpoints(this IEndpointRouteBuilder app)
        => app;
}
