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

    public static BuildArgsResolution Resolve(
        IReadOnlyList<KeyValuePair<string, string>>? templateBuildArgs,
        string instanceSlug,
        string? instanceHostname)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        if (templateBuildArgs is null || templateBuildArgs.Count == 0)
        {
            return new BuildArgsResolution(args, unresolved);
        }

        var host = string.IsNullOrWhiteSpace(instanceHostname) ? null : instanceHostname.Trim();
        foreach (var (key, value) in templateBuildArgs)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

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

            args[key] = resolved; // ultima definicion de una clave repetida gana
        }

        return new BuildArgsResolution(args, unresolved);
    }
}
