using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Modules.Projects.Domain.Secrets;
using Aethra.Shared.Contracts.Projects;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="IEnvironmentResolver"/>. Lee las env vars planas
/// (<c>projects.env_vars</c>) y los secretos cifrados (<c>projects.secrets</c>) del Instance y sus
/// scopes padre, y los fusiona aplicando la cascada Project → Template → Client → Instance.
///
/// Los secretos se descifran con <see cref="ISecretCodec"/> justo aquí; el valor en claro solo
/// viaja hacia el orquestador de deployment, que lo pasa al satélite. No se loguea.
/// </summary>
internal sealed class EfEnvironmentResolver(ProjectsDbContext db, ISecretCodec codec) : IEnvironmentResolver
{
    public async Task<IReadOnlyDictionary<string, string>> ResolveRuntimeEnvAsync(
        EnvironmentScopeChain scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Orden de aplicación: de menos a más específico. El último write gana, así que un valor
        // de Instance pisa al de Client, y dentro de un scope un secreto pisa a una env var.
        var levels = new (EnvScopeType Type, string Id)[]
        {
            (EnvScopeType.Project, scope.ProjectId),
            (EnvScopeType.Template, scope.TemplateId),
            (EnvScopeType.Client, scope.ClientId),
            (EnvScopeType.Instance, scope.InstanceId),
        };

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (type, id) in levels)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            // Solo las env vars marcadas IsRuntime se inyectan al contenedor (las IsBuildTime
            // alimentan los build args, no el runtime).
            var envVars = await db.EnvironmentVariables.AsNoTracking()
                .Where(e => e.ScopeType == type && e.ScopeId == id && e.IsRuntime)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var e in envVars)
            {
                result[e.Key] = e.Value;
            }

            var secrets = await db.Secrets.AsNoTracking()
                .Where(s => s.ScopeType == type && s.ScopeId == id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var s in secrets)
            {
                result[s.Key] = codec.Decode(s.ValueCipher);
            }
        }

        return result;
    }
}
