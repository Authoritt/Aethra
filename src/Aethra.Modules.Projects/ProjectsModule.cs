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
/// F9.0 persistence: registra el DbContext con sus aggregates (Project + Template + Client +
/// Instance + EnvironmentVariable) y las implementaciones EF reales de los lookups/writers
/// cross-module que consumen Services, Mcp, Cloudflare y Monitoring. El único stub que queda
/// es <see cref="NoOpSecretWriter"/> — F9.1 introducirá la tabla cifrada de secretos y su impl.
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

        // Read-models y writers cross-module (Shared.Contracts). Todos resuelven contra el
        // ProjectsDbContext del scope corriente — el TransactionBehavior del caller agrupa
        // los cambios del writer dentro de su transacción.
        services.AddScoped<ITemplateLookup, EfTemplateLookup>();
        services.AddScoped<IInstanceLookup, EfInstanceLookup>();
        services.AddScoped<ITenantContext, EfTenantContext>();
        services.AddScoped<IEnvVarWriter, EfEnvVarWriter>();
        // F9.1 cableará EfSecretWriter contra la nueva tabla cifrada.
        services.AddScoped<ISecretWriter, NoOpSecretWriter>();

        return services;
    }

    /// <summary>
    /// Stub. F9.5 reintroducirá endpoints REST sobre los nuevos commands/queries.
    /// </summary>
    public static IEndpointRouteBuilder MapProjectsModuleEndpoints(this IEndpointRouteBuilder app)
        => app;
}
