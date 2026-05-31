using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain;

/// <summary>
/// Origen Git de una <see cref="Application"/>. Para monorepos, <see cref="BaseDirectory"/>
/// y <see cref="WatchPaths"/> determinan qué subdirectorio se construye y cuándo redesplegar.
/// </summary>
public sealed class ApplicationSource
{
    public GitRepoUrl GitRepoUrl { get; private set; }
    public string Branch { get; private set; }
    public string WebhookSecret { get; private set; }

    /// <summary>
    /// Subdirectorio del repo que sirve como build context. <c>"/"</c> para repos no-monorepo.
    /// </summary>
    public string BaseDirectory { get; private set; }

    /// <summary>
    /// Globs estilo .gitignore que filtran qué paths del push disparan deploy.
    /// Ejemplo: <c>["backend/**", "shared/**"]</c>.
    /// Lista vacía = redespliega ante cualquier cambio en la rama.
    /// </summary>
    public IReadOnlyList<string> WatchPaths { get; private set; }

    public string? AccessTokenId { get; private set; }

    private ApplicationSource(
        GitRepoUrl gitRepoUrl,
        string branch,
        string webhookSecret,
        string baseDirectory,
        IReadOnlyList<string> watchPaths,
        string? accessTokenId)
    {
        GitRepoUrl = gitRepoUrl;
        Branch = branch;
        WebhookSecret = webhookSecret;
        BaseDirectory = baseDirectory;
        WatchPaths = watchPaths;
        AccessTokenId = accessTokenId;
    }

    public static ApplicationSource Create(
        GitRepoUrl gitRepoUrl,
        string branch,
        string? baseDirectory = null,
        IReadOnlyList<string>? watchPaths = null,
        string? accessTokenId = null)
    {
        var b = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
        var dir = NormalizeBaseDirectory(baseDirectory);
        var paths = watchPaths is { Count: > 0 } ? watchPaths : [];
        var secret = GenerateWebhookSecret();
        return new ApplicationSource(gitRepoUrl, b, secret, dir, paths, accessTokenId);
    }

    public void UpdateBranch(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException("Branch no puede estar vacío.", nameof(branch));
        }
        Branch = branch.Trim();
    }

    public void UpdateBaseDirectory(string? baseDirectory) => BaseDirectory = NormalizeBaseDirectory(baseDirectory);

    public void UpdateWatchPaths(IReadOnlyList<string> watchPaths) => WatchPaths = watchPaths;

    public void RotateWebhookSecret() => WebhookSecret = GenerateWebhookSecret();

    private static string NormalizeBaseDirectory(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "/";
        }
        var trimmed = input.Trim().Replace('\\', '/');
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }
        if (trimmed.Length > 1 && trimmed.EndsWith('/'))
        {
            trimmed = trimmed.TrimEnd('/');
        }
        return trimmed;
    }

    private static string GenerateWebhookSecret()
        => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    // Para EF Core (constructor sin args, materialización vía private setters).
    private ApplicationSource() : this(default!, "main", "", "/", [], null) { }
}
