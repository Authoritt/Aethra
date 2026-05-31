using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Helpers DI para el subsistema TLS. Llamar desde <c>ProxyModule.AddProxyModule</c>
/// después de registrar <c>ProxyDbContext</c>.
///
/// Registra:
/// <list type="bullet">
///   <item><see cref="IAcmeChallengeStore"/> como singleton (in-memory).</item>
///   <item><see cref="ICertManager"/> como scoped (Let's Encrypt + Certes).</item>
///   <item><see cref="ICertificateStore"/> como scoped (EF sobre el DbContext del módulo).</item>
///   <item><c>CertRenewalWorker</c> como hosted service.</item>
///   <item><see cref="TlsOptions"/> bindeado desde la sección <c>"Tls"</c>.</item>
///   <item>DataProtection si todavía no lo hizo el host.</item>
/// </list>
/// </summary>
public static class TlsRegistrationExtensions
{
    /// <summary>
    /// Registra TLS para un DbContext concreto <typeparamref name="TDbContext"/> (ProxyDbContext).
    /// Pasamos el tipo como genérico para no atar este shared-infrastructure ensamblado al DbContext concreto.
    /// </summary>
    public static IServiceCollection AddAethraTls<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TlsOptions>()
            .Bind(configuration.GetSection(TlsOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountEmail), "Tls:AccountEmail es obligatorio.")
            .Validate(o => o.RenewBeforeDays > 0, "Tls:RenewBeforeDays debe ser > 0.")
            .ValidateOnStart();

        // DataProtection: si el host ya lo registró se ignora. Aethra debería configurar el
        // KeyRing persistente (sistema de archivos o BD) en apps/api/Program.cs.
        services.AddDataProtection();

        services.AddSingleton<IAcmeChallengeStore, InMemoryAcmeChallengeStore>();

        // El store envuelve el DbContext concreto del módulo Proxy.
        services.AddScoped<ICertificateStore>(sp =>
            new EfCertificateStore(sp.GetRequiredService<TDbContext>()));

        services.AddScoped<ICertManager, LetsEncryptCertManager>();

        services.AddHostedService<CertRenewalWorker>();

        // TimeProvider: si alguien más ya lo registró, gana; sino usamos el sistema.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
