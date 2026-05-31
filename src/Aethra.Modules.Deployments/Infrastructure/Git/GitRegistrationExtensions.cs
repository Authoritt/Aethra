using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aethra.Modules.Deployments.Infrastructure.Git;

/// <summary>
/// Helpers DI para el subsistema de clones Git del módulo Deployments.
///
/// Se llama desde <c>DeploymentsModule.AddDeploymentsModule</c>. Es idempotente:
/// si el contenedor ya tiene un <see cref="IGitCloner"/> registrado (p.ej. un fake en tests),
/// no lo sobreescribe.
/// </summary>
public static class GitRegistrationExtensions
{
    /// <summary>
    /// Registra <see cref="IGitCloner"/> → <see cref="GitCloner"/> como singleton.
    /// El cloner es stateless: cada clone se aísla en su propio directorio temporal,
    /// por lo que no hay riesgo de compartir estado entre llamadas concurrentes.
    /// </summary>
    public static IServiceCollection AddAethraGit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IGitCloner, GitCloner>();
        return services;
    }
}
