using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Deployments.Infrastructure.Git;

/// <summary>
/// Helpers DI para el subsistema de clones Git del módulo Deployments.
///
/// Estado F9.0 cleanup: stub. <c>IGitCloner</c> y su impl LibGit2Sharp fueron borrados como
/// parte del refactor — F9.3 reintroducirá una abstracción nueva (probablemente delegada al
/// satélite remoto en vez de clonar en el central). Mantenemos esta extensión vacía para que
/// quien la llamase no rompa.
/// </summary>
public static class GitRegistrationExtensions
{
    /// <summary>
    /// No-op temporal. F9.3 registrará la nueva abstracción de clone Git.
    /// </summary>
    public static IServiceCollection AddAethraGit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
