using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.Presentation;
using IdAuth = Aethra.Modules.Identity.Infrastructure.Authentication;
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
/// F6: <see cref="ApiKey"/> persistidas con scopes para consumo desde el MCP server.
/// F11.1: multi-user con RBAC — <see cref="EfUserStore"/> + roles. El
/// <see cref="SingleUserStore"/> queda como fallback bootstrap cuando la BD no tiene users.
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

        // F11.1: codec de password + repos + store EF + seeder bootstrap.
        services.AddSingleton<IUserPasswordCodec, DataProtectionUserPasswordCodec>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<EfUserStore>();
        services.AddScoped<IdentitySeeder>();

        // F12.3: cross-module resolver para mapear PR.user.login → UserId.
        services.AddScoped<Aethra.Shared.Contracts.Identity.IGitHubUserResolver, EfGitHubUserResolver>();

        // F12.1B: codec dedicado para secretos TOTP + recovery codes + verifier.
        services.AddSingleton<ITotpSecretCodec, DataProtectionTotpSecretCodec>();
        services.AddSingleton<IdAuth.ITotpChallengeTokens, IdAuth.TotpChallengeTokens>();
        services.AddScoped<IdAuth.ITotpLoginVerifier, IdAuth.TotpLoginVerifier>();

        // MediatR escanea el assembly del modulo Identity (registrado en Program.cs central),
        // pero los handlers TOTP viven en este mismo assembly, asi que ya quedan registrados.

        return services;
    }

    /// <summary>
    /// Registra el scheme <c>"ApiKey"</c> en el authentication builder. Debe llamarse
    /// dentro de <c>builder.Services.AddAuthentication(...)</c> en Program.cs.
    /// </summary>
    public static AuthenticationBuilder AddAethraApiKeyAuth(this AuthenticationBuilder auth, string scheme)
        => auth.AddScheme<AethraApiKeyAuthOptions, AethraApiKeyAuthHandler>(scheme, _ => { });

    public static IEndpointRouteBuilder MapIdentityModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapApiKeysEndpoints();
        app.MapUsersEndpoints();
        app.MapTotpEndpoints();
        return app;
    }
}
