using System.Security.Cryptography;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Projects.Domain.EnvVars;

/// <summary>
/// Variables mágicas <c>${aethra.*}</c> que evitan que el usuario digite valores derivables:
/// <list type="bullet">
///   <item><c>aethra.app.url</c> — URL pública final de la app.</item>
///   <item><c>aethra.app.domain</c> — hostname principal.</item>
///   <item><c>aethra.app.container</c> — nombre del contenedor.</item>
///   <item><c>aethra.app.port</c> — puerto expuesto.</item>
///   <item><c>aethra.app.slug</c> — slug de la app.</item>
///   <item><c>aethra.env.name</c> — production / staging / ...</item>
///   <item><c>aethra.project.slug</c> — slug del proyecto.</item>
///   <item><c>aethra.vm.ip</c> — IP de la VM target.</item>
///   <item><c>aethra.random.password(N)</c> — password aleatorio de longitud N (default 32), persistido.</item>
/// </list>
/// </summary>
public sealed class MagicVariableProvider
{
    public Result<string> Resolve(string token, MagicVariableContext ctx)
    {
        // Forma con argumento: "aethra.random.password(32)"
        var parenIndex = token.IndexOf('(');
        var name = parenIndex >= 0 ? token[..parenIndex] : token;
        var arg = parenIndex >= 0 ? token[(parenIndex + 1)..].TrimEnd(')') : null;

        return name.ToLowerInvariant() switch
        {
            "aethra.app.url" => ctx.AppUrl ?? Unresolved(name),
            "aethra.app.domain" => ctx.AppDomain ?? Unresolved(name),
            "aethra.app.container" => ctx.AppContainerName ?? Unresolved(name),
            "aethra.app.port" => ctx.AppPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Unresolved(name),
            "aethra.app.slug" => ctx.AppSlug ?? Unresolved(name),
            "aethra.env.name" => ctx.EnvironmentName ?? Unresolved(name),
            "aethra.project.slug" => ctx.ProjectSlug ?? Unresolved(name),
            "aethra.vm.ip" => ctx.VmIp ?? Unresolved(name),
            "aethra.random.password" => RandomPassword(arg, ctx),
            _ => Error.Validation("envvar.magic_unknown", $"Variable mágica desconocida: ${{{token}}}"),
        };
    }

    private static Result<string> RandomPassword(string? lenArg, MagicVariableContext ctx)
    {
        var length = 32;
        if (!string.IsNullOrEmpty(lenArg)
            && int.TryParse(lenArg, out var parsed) && parsed is >= 8 and <= 256)
        {
            length = parsed;
        }

        // El contexto puede traer un "memoizer" persistente para que la misma key devuelva siempre
        // el mismo valor entre deploys (los passwords no deben rotar en cada build sin querer).
        if (ctx.RandomMemoizer is { } memoizer)
        {
            return memoizer($"random.password({length})", () => GenerateRandom(length));
        }
        return GenerateRandom(length);
    }

    private static string GenerateRandom(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new System.Text.StringBuilder(length);
        foreach (var b in bytes)
        {
            sb.Append(alphabet[b % alphabet.Length]);
        }
        return sb.ToString();
    }

    private static Result<string> Unresolved(string name)
        => Error.Validation("envvar.magic_unresolved",
            $"La variable mágica '${{{name}}}' requiere un contexto que no está disponible.");
}

/// <summary>
/// Contexto que provee la información del proyecto/env/app para resolver variables mágicas.
/// Campos null indican "no aplica" — usar la magic var producirá un error explícito.
/// </summary>
public sealed record MagicVariableContext(
    string? AppUrl = null,
    string? AppDomain = null,
    string? AppContainerName = null,
    int? AppPort = null,
    string? AppSlug = null,
    string? EnvironmentName = null,
    string? ProjectSlug = null,
    string? VmIp = null,
    Func<string, Func<string>, Result<string>>? RandomMemoizer = null);
