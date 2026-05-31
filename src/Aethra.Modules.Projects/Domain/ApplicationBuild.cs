namespace Aethra.Modules.Projects.Domain;

public enum BuildType
{
    Dockerfile = 0,
    DockerCompose = 1,
}

/// <summary>
/// Cómo se construye la imagen de la <see cref="Application"/>:
/// - <see cref="BuildType.Dockerfile"/>: ruta al Dockerfile dentro del <c>BaseDirectory</c>.
/// - <see cref="BuildType.DockerCompose"/>: ruta al <c>docker-compose.yml</c>; el orquestador
///   parsea y trata cada servicio como sub-recurso de la app.
/// </summary>
public sealed class ApplicationBuild
{
    public BuildType Type { get; private set; }
    public string Path { get; private set; }
    public IReadOnlyList<BuildArg> Args { get; private set; }

    private ApplicationBuild(BuildType type, string path, IReadOnlyList<BuildArg> args)
    {
        Type = type;
        Path = path;
        Args = args;
    }

    public static ApplicationBuild Dockerfile(string? path = null, IReadOnlyList<BuildArg>? args = null)
        => new(BuildType.Dockerfile, NormalizePath(path) ?? "Dockerfile", args ?? []);

    public static ApplicationBuild DockerCompose(string? path = null, IReadOnlyList<BuildArg>? args = null)
        => new(BuildType.DockerCompose, NormalizePath(path) ?? "docker-compose.yml", args ?? []);

    public void UpdateArgs(IReadOnlyList<BuildArg> args) => Args = args;

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        return path.Trim().Replace('\\', '/').TrimStart('/');
    }

    // EF Core
    private ApplicationBuild() : this(BuildType.Dockerfile, "Dockerfile", []) { }
}

/// <summary>
/// Argumento que se inyecta como <c>--build-arg</c> al construir la imagen.
/// Para secretos en build-time use envVars con <c>IsSecret=true</c> (BuildKit <c>--secret</c>).
/// </summary>
public sealed record BuildArg(string Key, string Value);
