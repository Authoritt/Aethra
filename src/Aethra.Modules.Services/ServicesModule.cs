using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Binding;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Modules.Services.Presentation;
using Aethra.Modules.Services.Templates;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Services;

/// <summary>
/// Punto de entrada del módulo Services.
///
/// Llamadas desde apps/api/Program.cs:
/// - <see cref="AddServicesModule"/> en builder.Services para DI.
/// - <see cref="MapServicesModuleEndpoints"/> en app después de UseAuthorization() para rutas REST.
/// </summary>
public static class ServicesModule
{
    public static IServiceCollection AddServicesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<ServicesDbContext>(conn);

        // Catálogo de plantillas one-click (postgres-16, redis-7, rabbitmq-3-mgmt) cargado
        // desde recursos embebidos al construir el singleton.
        services.AddSingleton<IServiceTemplateCatalog, EmbeddedServiceTemplateCatalog>();

        services.AddServicesProvisioners();

        // Codec de credenciales del binding y mapper de env vars (DATABASE_URL, etc.).
        services.AddSingleton<IBindingCredentialsCodec, DataProtectionBindingCredentialsCodec>();
        services.AddSingleton<IBindingEnvVarMapper, DefaultBindingEnvVarMapper>();

        return services;
    }

    public static IEndpointRouteBuilder MapServicesModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapServicesEndpoints();
}
