namespace Aethra.Modules.Deployments.Rollout;

public sealed record EnvMergeResult(
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<string> OverriddenKeys);

/// <summary>
/// OT-008 F1 — extrae el merge que T5 conectará en <c>NativeDeployRunner.DeployAsync</c> donde hoy
/// se copia <c>baseEnv</c> y luego <c>svc.Env</c> pisa claves con interpolación de <c>{instance}</c>
/// (líneas históricas 180-184).
/// </summary>
public static class EnvPrecedenceRules
{
    public static EnvMergeResult Merge(
        IReadOnlyDictionary<string, string> baseEnv,
        IEnumerable<KeyValuePair<string, string>> serviceEnv,
        string instanceSlug)
    {
        ArgumentNullException.ThrowIfNull(baseEnv);
        ArgumentNullException.ThrowIfNull(serviceEnv);

        var slug = instanceSlug ?? string.Empty;
        var env = new Dictionary<string, string>(baseEnv);
        var overridden = new List<string>();
        var seenOverrides = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kv in serviceEnv)
        {
            var value = (kv.Value ?? string.Empty).Replace("{instance}", slug, StringComparison.Ordinal);
            if (env.TryGetValue(kv.Key, out var previous)
                && !string.Equals(previous, value, StringComparison.Ordinal)
                && seenOverrides.Add(kv.Key))
            {
                overridden.Add(kv.Key);
            }

            env[kv.Key] = value;
        }

        return new EnvMergeResult(env, overridden);
    }
}
