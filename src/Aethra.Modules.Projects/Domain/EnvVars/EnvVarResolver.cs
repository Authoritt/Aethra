using System.Text.RegularExpressions;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Projects.Domain.EnvVars;

/// <summary>
/// Resuelve el conjunto efectivo de variables de entorno para una Application:
///   1. Merge por scope (Application gana sobre Environment, que gana sobre Project).
///   2. Interpolación recursiva de <c>${other}</c> y de "variables mágicas" <c>${aethra.*}</c>
///      provistas por <see cref="MagicVariableProvider"/>.
///   3. Variables con <c>IsLiteral=true</c> NO se interpolan.
///
/// Detección de ciclos: límite de 16 expansiones por variable.
/// </summary>
public sealed partial class EnvVarResolver(MagicVariableProvider magic)
{
    private const int MaxDepth = 16;

    public Result<EnvVarResolution> Resolve(EnvVarResolutionRequest request)
    {
        var byKey = new Dictionary<string, EnvironmentVariable>(StringComparer.Ordinal);

        // Orden de prioridad inversa: primero project, luego env, luego app.
        // Los más específicos sobreescriben.
        foreach (var v in request.ProjectVars)
        {
            byKey[v.Key] = v;
        }
        foreach (var v in request.EnvironmentVars)
        {
            byKey[v.Key] = v;
        }
        foreach (var v in request.ApplicationVars)
        {
            byKey[v.Key] = v;
        }

        // Filtrar por scope solicitado (build-time vs runtime).
        var effective = new Dictionary<string, ResolvedVar>(StringComparer.Ordinal);
        foreach (var (key, v) in byKey)
        {
            var include = request.Scope switch
            {
                ResolutionScope.BuildTime => v.IsBuildTime,
                ResolutionScope.Runtime => v.IsRuntime,
                ResolutionScope.Both => v.IsBuildTime || v.IsRuntime,
                _ => true,
            };
            if (!include)
            {
                continue;
            }
            effective[key] = new ResolvedVar(key, v.Value, v.IsLiteral, v.IsSecret);
        }

        // Interpolación.
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, v) in effective)
        {
            if (v.IsLiteral)
            {
                output[key] = v.RawValue;
                continue;
            }
            var expandResult = Expand(v.RawValue, effective, magic, request.MagicContext, depth: 0);
            if (expandResult.IsFailure)
            {
                return expandResult.Error;
            }
            output[key] = expandResult.Value;
        }

        return new EnvVarResolution(output, effective.Values
            .Where(v => v.IsSecret)
            .Select(v => v.Key)
            .ToHashSet());
    }

    private static Result<string> Expand(
        string value,
        IDictionary<string, ResolvedVar> vars,
        MagicVariableProvider magic,
        MagicVariableContext magicContext,
        int depth)
    {
        if (depth > MaxDepth)
        {
            return Error.Failure("envvar.cycle", $"Ciclo de interpolación detectado tras {MaxDepth} niveles.");
        }
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < value.Length)
        {
            var match = InterpolationRegex().Match(value, i);
            if (!match.Success)
            {
                sb.Append(value, i, value.Length - i);
                break;
            }
            sb.Append(value, i, match.Index - i);
            var token = match.Groups[1].Value;

            string? replacement = null;

            if (token.StartsWith("aethra.", StringComparison.OrdinalIgnoreCase))
            {
                var magicResult = magic.Resolve(token, magicContext);
                if (magicResult.IsFailure)
                {
                    return magicResult.Error;
                }
                replacement = magicResult.Value;
            }
            else if (vars.TryGetValue(token, out var other) && !other.IsLiteral)
            {
                var nested = Expand(other.RawValue, vars, magic, magicContext, depth + 1);
                if (nested.IsFailure)
                {
                    return nested.Error;
                }
                replacement = nested.Value;
            }
            else if (vars.TryGetValue(token, out var literalOther))
            {
                replacement = literalOther.RawValue;
            }

            if (replacement is null)
            {
                // Variable no resuelta: mantener literal el token (comportamiento docker-compose).
                sb.Append(match.Value);
            }
            else
            {
                sb.Append(replacement);
            }
            i = match.Index + match.Length;
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\$\{([a-zA-Z0-9_.()-]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex InterpolationRegex();

    private sealed record ResolvedVar(string Key, string RawValue, bool IsLiteral, bool IsSecret);
}

public enum ResolutionScope
{
    BuildTime = 0,
    Runtime = 1,
    Both = 2,
}

public sealed record EnvVarResolutionRequest(
    IReadOnlyList<EnvironmentVariable> ProjectVars,
    IReadOnlyList<EnvironmentVariable> EnvironmentVars,
    IReadOnlyList<EnvironmentVariable> ApplicationVars,
    MagicVariableContext MagicContext,
    ResolutionScope Scope = ResolutionScope.Both);

public sealed record EnvVarResolution(
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlySet<string> SecretKeys);
