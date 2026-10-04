using System.Text.RegularExpressions;

namespace Aethra.Modules.Services.Templates;

/// <summary>
/// Sustituye placeholders <c>${nombre}</c> en strings, diccionarios y listas con los valores
/// del binding (típicamente <c>admin_user</c> y <c>admin_password</c>).
///
/// Si una variable no está presente en <c>values</c>, se deja el placeholder tal cual:
/// preferimos no fallar para que el caller pueda interpolar en varias pasadas.
/// </summary>
public static partial class TemplateInterpolator
{
    [GeneratedRegex(@"\$\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    /// <summary>Reemplaza placeholders en un string individual.</summary>
    public static string Apply(string input, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(values);
        if (input.Length == 0)
        {
            return input;
        }

        return PlaceholderRegex().Replace(input, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var replacement) ? replacement : match.Value;
        });
    }

    /// <summary>Reemplaza placeholders en cada valor del diccionario; las claves no se tocan.</summary>
    public static IReadOnlyDictionary<string, string> Apply(
        IReadOnlyDictionary<string, string> input,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(values);

        var result = new Dictionary<string, string>(input.Count, StringComparer.Ordinal);
        foreach (var (key, value) in input)
        {
            result[key] = Apply(value, values);
        }
        return result;
    }

    /// <summary>Reemplaza placeholders en cada elemento de la lista (preserva null).</summary>
    public static IReadOnlyList<string>? Apply(
        IReadOnlyList<string>? input,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (input is null)
        {
            return null;
        }

        var result = new string[input.Count];
        for (var i = 0; i < input.Count; i++)
        {
            result[i] = Apply(input[i], values);
        }
        return result;
    }

    /// <summary>
    /// OT-008 T15: resuelve el healthcheck de la plantilla con los mismos valores que env/command. Antes
    /// el <c>Test</c> viajaba literal (<c>pg_isready -U ${admin_user}</c>) y el contenedor sano salía
    /// <c>unhealthy</c>. Solo aplica a servicios creados desde ahora: los existentes conservan el suyo hasta recrearse.
    /// </summary>
    public static Aethra.Shared.Contracts.Containers.HealthcheckSpec? ApplyHealthcheck(
        TemplateHealthcheck? healthcheck,
        IReadOnlyDictionary<string, string> values)
    {
        if (healthcheck is null)
        {
            return null;
        }

        return new Aethra.Shared.Contracts.Containers.HealthcheckSpec(
            Apply(healthcheck.Test, values)!,
            healthcheck.IntervalSeconds,
            healthcheck.Retries,
            TimeoutSeconds: null,
            StartPeriodSeconds: null);
    }
}
