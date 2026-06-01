using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.Infrastructure.Credentials;
using Aethra.Modules.Settings.Infrastructure.Persistence;
using Aethra.Modules.Settings.Presentation;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Settings;

/// <summary>
/// Punto de entrada del módulo Settings. Registra:
/// <list type="bullet">
///   <item><c>SettingsDbContext</c> con schema <c>settings</c>.</item>
///   <item>Codec DataProtection de credenciales con purpose <c>aethra-integration-creds</c>.</item>
///   <item>Implementaciones EF de los contratos cross-module (resolver, base domain, catálogo).</item>
///   <item>Handlers MediatR (autoscan en Program.cs) + validators FluentValidation.</item>
/// </list>
///
/// Wiring en apps/api/Program.cs:
/// 1. <c>services.AddSettingsModule(configuration)</c>.
/// 2. <c>app.MapSettingsModuleEndpoints()</c> tras <c>UseAuthorization</c>.
/// </summary>
public static class SettingsModule
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");
        services.AddAethraModuleDbContext<SettingsDbContext>(conn);

        services.AddSingleton<IIntegrationCredentialCodec, DataProtectionIntegrationCredentialCodec>();
        services.AddScoped<IIntegrationCredentialResolver, EfIntegrationCredentialResolver>();
        services.AddScoped<IBaseDomainProvider, EfBaseDomainProvider>();
        services.AddScoped<IEnvironmentCatalog, EfEnvironmentCatalog>();

        return services;
    }

    public static IEndpointRouteBuilder MapSettingsModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapSettingsEndpoints();
}
