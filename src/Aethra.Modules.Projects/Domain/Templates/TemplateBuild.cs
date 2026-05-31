namespace Aethra.Modules.Projects.Domain.Templates;

/// <summary>
/// Estrategia de construcción de la imagen para un <see cref="Template"/>.
/// </summary>
public enum TemplateBuildType
{
    Dockerfile = 0,
    DockerCompose = 1,
    Nixpacks = 2,
}

/// <summary>
/// Argumento que se inyecta como <c>--build-arg</c> al construir la imagen.
/// Para secretos en build-time use envVars con <c>IsSecret=true</c> (BuildKit <c>--secret</c>).
/// </summary>
/// <remarks>
/// Se modela como sealed record (no record struct) para mantener simetría con
/// <see cref="TemplateBuild"/> (ambos son owned entities desde la óptica de EF). Como
/// <see cref="TemplateBuild.BuildArgs"/> es <c>IReadOnlyList&lt;KeyValuePair&lt;string,string&gt;&gt;</c>
/// los args se persistirán como columna JSON serializada — pero internamente se exponen como
/// <c>KeyValuePair</c> para evitar acoplar a EF en el contrato de dominio.
/// </remarks>
public sealed record TemplateBuildArg(string Key, string Value);

/// <summary>
/// Cómo se construye la imagen de un <see cref="Template"/>:
/// - <see cref="TemplateBuildType.Dockerfile"/>: ruta al Dockerfile dentro del <c>BaseDirectory</c>.
/// - <see cref="TemplateBuildType.DockerCompose"/>: ruta al <c>docker-compose.yml</c>; el orquestador
///   parsea y trata cada servicio como sub-recurso del template.
/// - <see cref="TemplateBuildType.Nixpacks"/>: detección automática (no requiere Dockerfile).
/// </summary>
public sealed class TemplateBuild
{
    public TemplateBuildType BuildType { get; private set; }
    public string DockerfilePath { get; private set; }
    public string? ComposeFilePath { get; private set; }

    /// <summary>
    /// Args build-time, expuestos como <c>KeyValuePair</c> para no acoplar a un VO específico desde
    /// el contrato. Internamente, EF los persistirá como columna JSON.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> BuildArgs { get; private set; }

    private TemplateBuild(
        TemplateBuildType buildType,
        string dockerfilePath,
        string? composeFilePath,
        IReadOnlyList<KeyValuePair<string, string>> buildArgs)
    {
        BuildType = buildType;
        DockerfilePath = dockerfilePath;
        ComposeFilePath = composeFilePath;
        BuildArgs = buildArgs;
    }

    public static TemplateBuild Dockerfile(
        string? dockerfilePath = null,
        IReadOnlyList<KeyValuePair<string, string>>? buildArgs = null)
        => new(
            TemplateBuildType.Dockerfile,
            NormalizePath(dockerfilePath) ?? "Dockerfile",
            null,
            buildArgs ?? []);

    public static TemplateBuild DockerCompose(
        string? composeFilePath = null,
        IReadOnlyList<KeyValuePair<string, string>>? buildArgs = null)
        => new(
            TemplateBuildType.DockerCompose,
            "Dockerfile",
            NormalizePath(composeFilePath) ?? "docker-compose.yml",
            buildArgs ?? []);

    public static TemplateBuild Nixpacks(IReadOnlyList<KeyValuePair<string, string>>? buildArgs = null)
        => new(TemplateBuildType.Nixpacks, "Dockerfile", null, buildArgs ?? []);

    public void UpdateBuildArgs(IReadOnlyList<KeyValuePair<string, string>> buildArgs) => BuildArgs = buildArgs;

    public void UpdateDockerfilePath(string? dockerfilePath)
        => DockerfilePath = NormalizePath(dockerfilePath) ?? "Dockerfile";

    public void UpdateComposeFilePath(string? composeFilePath) => ComposeFilePath = NormalizePath(composeFilePath);

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        return path.Trim().Replace('\\', '/').TrimStart('/');
    }

    // EF Core
    private TemplateBuild() : this(TemplateBuildType.Dockerfile, "Dockerfile", null, []) { }
}
