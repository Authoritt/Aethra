namespace Aethra.Modules.Deployments.Rollout;

/// <summary>
/// OT-069 F2 — decide qué contenedores puede barrer el deploy nativo al final de un rollout sano.
///
/// <para>
/// El fallo real fue destructivo: <c>NativeDeployRunner</c> borraba cualquier <c>{slug}-*</c> que no
/// fuera un servicio del template, y así mató un servicio gestionado llamado <c>yunke-redis</c>.
/// Esta regla mantiene el barrido de zombis de app, pero antes excluye los contenedores registrados
/// como servicios gestionados en la VM. Sin esa fuente de propiedad, el nombre no alcanza.
/// </para>
/// </summary>
public static class ContainerCleanupRules
{
    public static IReadOnlyList<string> SelectStaleContainerNames(
        IEnumerable<string> containerNames,
        string slug,
        IEnumerable<string> templateServiceNames,
        IEnumerable<string> managedServiceContainerNames,
        IEnumerable<string>? otherInstanceSlugsOnVm = null)
    {
        ArgumentNullException.ThrowIfNull(containerNames);
        ArgumentNullException.ThrowIfNull(templateServiceNames);
        ArgumentNullException.ThrowIfNull(managedServiceContainerNames);

        if (string.IsNullOrWhiteSpace(slug))
        {
            return [];
        }

        var normalizedSlug = slug.Trim();
        var prefix = $"{normalizedSlug}-";
        var activeTemplateContainers = templateServiceNames
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => $"{normalizedSlug}-{s.Trim()}")
            .ToHashSet(StringComparer.Ordinal);
        var managedContainers = managedServiceContainerNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var otherInstancePrefixes = (otherInstanceSlugsOnVm ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(s => !string.Equals(s, normalizedSlug, StringComparison.Ordinal))
            .Where(s => s.StartsWith(prefix, StringComparison.Ordinal))
            .Select(s => $"{s}-")
            .ToHashSet(StringComparer.Ordinal);

        return [.. containerNames
            .Where(n => ShouldRemove(n, prefix, activeTemplateContainers, managedContainers, otherInstancePrefixes))
            .Distinct(StringComparer.Ordinal)];
    }

    private static bool ShouldRemove(
        string? containerName,
        string prefix,
        HashSet<string> activeTemplateContainers,
        HashSet<string> managedContainers,
        HashSet<string> otherInstancePrefixes)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return false;
        }

        var name = containerName.Trim();
        return name.StartsWith(prefix, StringComparison.Ordinal)
            && !activeTemplateContainers.Contains(name)
            && !managedContainers.Contains(name)
            && !otherInstancePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }
}
