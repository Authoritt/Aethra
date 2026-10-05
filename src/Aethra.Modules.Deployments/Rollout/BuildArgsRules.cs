namespace Aethra.Modules.Deployments.Rollout;

/// <param name="Args">Build args ya interpolados, listos para <c>BuildSpec.BuildArgs</c>.</param>
/// <param name="UnresolvedDomainKeys">Claves cuyo valor usa <c>{domain}</c> y la instancia no tiene host.</param>
public sealed record BuildArgsResolution(
    IReadOnlyDictionary<string, string> Args,
    IReadOnlyList<string> UnresolvedDomainKeys);

/// <summary>
/// OT-008 T12 — decide los <c>--build-arg</c> del build git a partir de los BuildArgs del Template.
/// Tokens: <c>{instance}</c> = slug de la instancia; <c>{domain}</c> = host de la instancia
/// (<c>CustomDomain ?? AutoHostname</c>, mismo criterio que <see cref="ServiceHostRules"/>).
/// Un template SIN BuildArgs produce un diccionario vacio: el build sale identico al historico.
/// </summary>
public static class BuildArgsRules
{
    public const string InstanceToken = "{instance}";

    /// <summary>
    /// Build args finales = BuildArgs del template + env vars build-time EFECTIVAS de la instancia
    /// (<paramref name="instanceBuildTimeEnv"/>, ya resueltas por la cascada project→template→
    /// client→instance; sin secretos). PRECEDENCIA: la env var de la instancia GANA sobre el
    /// BuildArg del template para la misma clave (es mas especifica: el template lo comparten prod
    /// y staging con valores distintos). Ambos pasan por la misma interpolacion y la misma regla de
    /// fallo explicito de <c>{domain}</c> sin host.
    /// </summary>
    public static BuildArgsResolution Resolve(
        IReadOnlyList<KeyValuePair<string, string>>? templateBuildArgs,
        string instanceSlug,
        string? instanceHostname,
        IReadOnlyDictionary<string, string>? instanceBuildTimeEnv = null)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        var unresolved = new List<string>();

        // Valores crudos: template primero, env de la instancia despues (pisa). Se interpola una vez.
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in templateBuildArgs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                raw[key] = value ?? string.Empty; // ultima definicion de una clave repetida gana
            }
        }

        foreach (var (key, value) in instanceBuildTimeEnv ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                raw[key] = value ?? string.Empty;
            }
        }

        if (raw.Count == 0)
        {
            return new BuildArgsResolution(args, unresolved);
        }

        var host = string.IsNullOrWhiteSpace(instanceHostname) ? null : instanceHostname.Trim();
        foreach (var (key, value) in raw)
        {
            var resolved = (value ?? string.Empty).Replace(InstanceToken, instanceSlug, StringComparison.Ordinal);
            if (resolved.Contains(ServiceHostRules.DomainToken, StringComparison.Ordinal))
            {
                if (host is null)
                {
                    unresolved.Add(key);
                    continue;
                }

                resolved = resolved.Replace(ServiceHostRules.DomainToken, host, StringComparison.Ordinal);
            }

            args[key] = resolved;
        }

        return new BuildArgsResolution(args, unresolved);
    }
}
