using System.Text.RegularExpressions;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Shared.Kernel.Primitives;

/// <summary>
/// URL de repo Git en formato HTTPS o SSH.
/// Ejemplos:
///   https://github.com/user/repo
///   https://github.com/user/repo.git
///   git@github.com:user/repo.git
///   ssh://git@github.com/user/repo.git
/// </summary>
public readonly partial record struct GitRepoUrl
{
    public string Value { get; }
    public GitRepoUrlKind Kind { get; }

    private GitRepoUrl(string value, GitRepoUrlKind kind)
    {
        Value = value;
        Kind = kind;
    }

    public override string ToString() => Value;

    public static Result<GitRepoUrl> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("git.empty", "La URL del repo Git no puede estar vacía.");
        }
        var v = input.Trim();

        if (HttpsRegex().IsMatch(v))
        {
            return new GitRepoUrl(v, GitRepoUrlKind.Https);
        }
        if (SshScpRegex().IsMatch(v))
        {
            return new GitRepoUrl(v, GitRepoUrlKind.SshScp);
        }
        if (SshUrlRegex().IsMatch(v))
        {
            return new GitRepoUrl(v, GitRepoUrlKind.SshUrl);
        }

        return Error.Validation(
            "git.format",
            "URL inválida. Acepta https://host/path o git@host:path o ssh://git@host/path.");
    }

    /// <summary>
    /// Devuelve el nombre sugerido del repo (último segmento del path, sin <c>.git</c>).
    /// </summary>
    public string SuggestRepoName()
    {
        var v = Value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? Value[..^4]
            : Value;

        var sepIndex = v.LastIndexOfAny(['/', ':']);
        return sepIndex >= 0 && sepIndex < v.Length - 1 ? v[(sepIndex + 1)..] : v;
    }

    [GeneratedRegex(@"^https?://[^\s]+?(\.git)?$", RegexOptions.CultureInvariant)]
    private static partial Regex HttpsRegex();

    [GeneratedRegex(@"^[\w.\-]+@[\w.\-]+:[\w./\-]+?(\.git)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SshScpRegex();

    [GeneratedRegex(@"^ssh://[\w.\-]+@[\w.\-]+/[\w./\-]+?(\.git)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SshUrlRegex();
}

public enum GitRepoUrlKind
{
    Https = 0,
    SshScp = 1,
    SshUrl = 2,
}
