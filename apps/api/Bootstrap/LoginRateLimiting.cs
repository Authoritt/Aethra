using System.Globalization;
using System.Threading.RateLimiting;
using Aethra.Shared.Contracts.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// OT-012 — limitador propio del login del panel (<c>/auth/login</c> y <c>/auth/login/totp</c>).
/// Defensa en profundidad: la regla de Cloudflare protege el borde, pero H1 de OT-011 demostro
/// que el borde se puede saltar por otro hostname. Particiona por IP real
/// (<see cref="ClientAddress.ForRateLimit"/>). Solo afecta a los endpoints que declaran la
/// politica: el proxy de las apps no tiene limitador global.
/// </summary>
public static class LoginRateLimiting
{
    public const string PolicyName = "aethra-login";

    public static IServiceCollection AddAethraLoginRateLimiter(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var permit = Math.Max(1, configuration.GetValue("Panel:LoginRateLimit:PermitLimit", 10));
        var window = TimeSpan.FromSeconds(
            Math.Max(1, configuration.GetValue("Panel:LoginRateLimit:WindowSeconds", 300)));
        var peers = PanelPeers.FromConfiguration(configuration);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Aethra.Auth.RateLimit")
                    .LogWarning("Login limitado para {Client} en {Path}",
                        ClientAddress.ForRateLimit(ctx.HttpContext, peers) ?? "local",
                        ctx.HttpContext.Request.Path);
                return ValueTask.CompletedTask;
            };
            options.AddPolicy(PolicyName, http => ClientAddress.ForRateLimit(http, peers) is { } key
                ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permit,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                })
                // Par local sin CF-Connecting-IP: automatizacion de la maquina (G2 de OT-012).
                : RateLimitPartition.GetNoLimiter("local"));
        });
        return services;
    }
}

/// <summary>
/// GH-33 / B1 — Limitador para los endpoints de sondeo SSH (/ssh/probe y /{vmId}/ssh/probe).
/// Reutiliza el mismo patron de particion por IP real tras Cloudflare (<see cref="ClientAddress.ForRateLimit"/>).
/// Solo AGREGA su politica: el <c>RejectionStatusCode</c> (429) y el <c>OnRejected</c> globales son
/// los del login; esta politica trae su propio <c>OnRejected</c> para no pisar el log del login
/// (<c>AddRateLimiter</c> acumula configuraciones y la ultima en asignar <c>OnRejected</c> gana).
/// </summary>
public static class SshProbeRateLimiting
{
    public const string PolicyName = Aethra.Modules.Vms.Presentation.VmsEndpoints.SshProbeRateLimitPolicy;

    public static IServiceCollection AddAethraSshProbeRateLimiter(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var permit = Math.Max(1, configuration.GetValue("Panel:ProbeRateLimit:PermitLimit", 10));
        var window = TimeSpan.FromSeconds(
            Math.Max(1, configuration.GetValue("Panel:ProbeRateLimit:WindowSeconds", 60)));
        var peers = PanelPeers.FromConfiguration(configuration);

        services.AddRateLimiter(options =>
            options.AddPolicy(PolicyName, new SshProbePolicy(permit, window, peers)));
        return services;
    }

    private sealed class SshProbePolicy(int permit, TimeSpan window, PanelPeers peers) : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => (ctx, _) =>
        {
            if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                ctx.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }
            ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Aethra.Vms.ProbeRateLimit")
                .LogWarning("Sondeo SSH limitado para {Client} en {Path}",
                    ClientAddress.ForRateLimit(ctx.HttpContext, peers) ?? "local",
                    ctx.HttpContext.Request.Path);
            return ValueTask.CompletedTask;
        };

        public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
            ClientAddress.ForRateLimit(httpContext, peers) is { } key
                ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permit,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                })
                // Par local sin CF-Connecting-IP: automatizacion de la maquina (mismo criterio que el login).
                : RateLimitPartition.GetNoLimiter("local");
    }
}

/// <summary>
/// OT-012 (H2 de OT-011) — <c>/openapi</c> exige una sesion/clave de ADMIN (scope <c>*</c>) con
/// independencia de <c>ASPNETCORE_ENVIRONMENT</c>: produccion corre en Development a proposito
/// (AETHRA-DEPLOY-CONTEXT.md) y antes eso dejaba el documento anonimo.
/// </summary>
public static class OpenApiAccess
{
    public const string PolicyName = "OpenApiAdmin";

    public static AuthorizationOptions AddOpenApiAdminPolicy(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(PolicyName, p => p
            .AddAuthenticationSchemes(AuthSchemes.Cookie, AuthSchemes.ApiKey)
            .RequireAuthenticatedUser()
            .RequireClaim(ApiKeyAuthSchemes.ScopeClaim, ApiKeyAuthSchemes.AdminScope));
        return options;
    }

    public static IEndpointConventionBuilder MapAethraOpenApi(this IEndpointRouteBuilder app)
        => app.MapOpenApi().RequireAuthorization(PolicyName);
}
