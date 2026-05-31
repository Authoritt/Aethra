using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Cloudflare;

/// <summary>
/// Punto de entrada del modulo Cloudflare.
/// Registra:
/// <list type="bullet">
///   <item><c>CloudflareDbContext</c> con schema <c>cloudflare</c>.</item>
///   <item>Codec de tokens API basado en DataProtection.</item>
///   <item>Named <c>HttpClient</c> "Cloudflare" con BaseAddress <c>api.cloudflare.com/client/v4/</c>.</item>
///   <item>Handlers MediatR (autoscan en Program.cs) + validators FluentValidation.</item>
/// </list>
///
/// Wiring en apps/api/Program.cs:
/// 1. <c>services.AddCloudflareModule(configuration)</c>.
/// 2. <c>app.MapCloudflareModuleEndpoints()</c> tras UseAuthorization.
/// </summary>
public static class CloudflareModule
{
    public static IServiceCollection AddCloudflareModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<CloudflareDbContext>(conn);

        services.AddSingleton<ICloudflareTokenCodec, DataProtectionCloudflareTokenCodec>();

        services.AddHttpClient<ICloudflareApiClient, HttpCloudflareApiClient>(http =>
        {
            http.BaseAddress = HttpCloudflareApiClient.DefaultBaseAddress;
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    public static IEndpointRouteBuilder MapCloudflareModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapCloudflareEndpoints();
}
