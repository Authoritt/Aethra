using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Modules.Projects.Infrastructure.Security;
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
        // ProjectsDbContext del scope corriente. Cada writer llama SaveChangesAsync por sí
        // mismo: no hay transacción cross-DbContext entre el caller (p.ej. ServicesDbContext)
        // y este ProjectsDbContext, así que invocar el writer ES un punto-de-no-retorno.
        services.AddScoped<ITemplateLookup, EfTemplateLookup>();
        services.AddScoped<IInstanceLookup, EfInstanceLookup>();
        services.AddScoped<ITenantContext, EfTenantContext>();
        services.AddScoped<IEnvVarWriter, EfEnvVarWriter>();
        // F9.1 cableará EfSecretWriter contra la nueva tabla cifrada.
        services.AddScoped<ISecretWriter, NoOpSecretWriter>();

        // F9.9: codec del Template.WebhookSecret. DataProtection ya está registrado en
        // apps/api/Program.cs con KeyRing persistente.
        services.AddSingleton<IWebhookSecretCodec, DataProtectionWebhookSecretCodec>();

        return services;
    }

    /// <summary>
    /// Mapea los endpoints REST del módulo (F9.5): projects, templates, clients, instances.
    /// </summary>
    public static IEndpointRouteBuilder MapProjectsModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapProjectsEndpoints();
}
