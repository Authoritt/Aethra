using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Identity;

/// <summary>
/// Punto de entrada del módulo Identity.
///
/// - <c>SingleUserStore</c>: credenciales single-user en memoria (cookie login).
/// - <see cref="ApiKey"/>: persistidas en BD con scopes, hash determinístico para lookup O(log n).
/// </summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentityOptions>(configuration.GetSection("Identity"));
        services.AddSingleton<SingleUserStore>();

        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");
        services.AddAethraModuleDbContext<IdentityDbContext>(conn);

        services.AddSingleton<IApiKeyHasher, ApiKeyHasher>();
        services.AddScoped<IApiKeyRepository, EfApiKeyRepository>();

        return services;
    }

    /// <summary>
    /// Registra el scheme <c>"ApiKey"</c> en el authentication builder. Debe llamarse
    /// dentro de <c>builder.Services.AddAuthentication(...)</c> en Program.cs.
    /// </summary>
    public static AuthenticationBuilder AddAethraApiKeyAuth(this AuthenticationBuilder auth, string scheme)
        => auth.AddScheme<AethraApiKeyAuthOptions, AethraApiKeyAuthHandler>(scheme, _ => { });

    public static IEndpointRouteBuilder MapIdentityModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapApiKeysEndpoints();
}