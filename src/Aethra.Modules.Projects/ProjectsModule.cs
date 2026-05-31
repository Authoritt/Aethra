using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Modules.Projects.Presentation;
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
/// Llamadas desde apps/api/Program.cs:
/// - <see cref="AddProjectsModule"/> en builder.Services para DI.
/// - <see cref="MapProjectsModuleEndpoints"/> en app después de UseAuthorization() para rutas REST.
/// </summary>
public static class ProjectsModule
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<ProjectsDbContext>(conn);

        // Read-model lookup expuesto cross-module (Shared.Contracts):
        services.AddScoped<IApplicationLookup, ApplicationLookup>();
        services.AddScoped<IEnvVarWriter, EnvVarWriter>();

        return services;
    }

    public static IEndpointRouteBuilder MapProjectsModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapProjectsEndpoints();
}
