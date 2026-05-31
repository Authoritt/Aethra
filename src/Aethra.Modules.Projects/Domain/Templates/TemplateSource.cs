using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain.Templates;

/// <summary>
/// Origen Git de un <see cref="Template"/>. Para monorepos, <see cref="BaseDirectory"/>
/// y <see cref="WatchPaths"/> determinan qué subdirectorio se construye y cuándo redesplegar.
/// </summary>
/// <remarks>
/// Sealed class (no record struct) por dos razones:
/// 1. Persistirá como entity owned por EF Core (no como JSON column) para poder indexar por
///    <c>GitRepoUrl</c> al matchear webhooks; un record struct se trataría como complex value.
/// 2. <see cref="WatchPaths"/> es <c>IReadOnlyList&lt;string&gt;</c>: EF lo serializará a JSON
///    en una columna <c>watch_paths_json</c> mediante <c>HasConversion</c> + <c>JsonSerializer</c>
///    (config a definir en F9.2 cuando se reescriba el DbContext).
/// </remarks>
public sealed class TemplateSource
{
    public GitRepoUrl GitRepoUrl { get; private set; }
    public string Branch { get; private set; }

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

    /// <summary>
    /// Nombre lógico de la credencial (Personal Access Token, deploy key) almacenada en el módulo
    /// <c>Settings</c> (F9.1). <c>null</c> = repo público o ya accesible vía agent.
    /// El secret real nunca vive en el aggregate.
    /// </summary>
    public string? AccessTokenCredentialName { get; private set; }

    private TemplateSource(
        GitRepoUrl gitRepoUrl,
        string branch,
        string baseDirectory,
        IReadOnlyList<string> watchPaths,
        string? accessTokenCredentialName)
    {
        GitRepoUrl = gitRepoUrl;
        Branch = branch;
        BaseDirectory = baseDirectory;
        WatchPaths = watchPaths;
        AccessTokenCredentialName = accessTokenCredentialName;
    }

    public static TemplateSource Create(
        GitRepoUrl gitRepoUrl,
        string branch,
        string? baseDirectory = null,
        IReadOnlyList<string>? watchPaths = null,
        string? accessTokenCredentialName = null)
    {
        var b = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
        var dir = NormalizeBaseDirectory(baseDirectory);
        var paths = watchPaths is { Count: > 0 } ? watchPaths : [];
        var token = string.IsNullOrWhiteSpace(accessTokenCredentialName) ? null : accessTokenCredentialName.Trim();
        return new TemplateSource(gitRepoUrl, b, dir, paths, token);
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

    public void UpdateAccessTokenCredentialName(string? credentialName)
        => AccessTokenCredentialName = string.IsNullOrWhiteSpace(credentialName) ? null : credentialName.Trim();

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

    // EF Core
    private TemplateSource() : this(default!, "main", "/", [], null) { }
}
