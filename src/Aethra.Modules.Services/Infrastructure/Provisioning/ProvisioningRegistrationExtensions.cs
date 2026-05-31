using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

public static class ProvisioningRegistrationExtensions
{
    public static IServiceCollection AddServicesProvisioners(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAdminCredentialsCodec, DataProtectionAdminCredentialsCodec>();
        services.TryAddSingleton<IManagedServiceHostResolver, DirectContainerNameResolver>();

        // El factory de HttpClient es usado por RabbitProvisioner; idempotente si el host ya lo registró.
        services.AddHttpClient();

        services.AddScoped<IServiceProvisioner, PostgresProvisioner>();
        services.AddScoped<IServiceProvisioner, RedisProvisioner>();
        services.AddScoped<IServiceProvisioner, RabbitProvisioner>();

        return services;
    }
}
