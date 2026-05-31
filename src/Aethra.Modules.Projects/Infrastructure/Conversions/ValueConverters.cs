using Aethra.Modules.Projects.Domain;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

// Alias para evitar conflicto con System.ApplicationId (Manifest-style runtime ID, no usado aquí).
using ApplicationId = Aethra.Modules.Projects.Domain.ApplicationId;

namespace Aethra.Modules.Projects.Infrastructure.Conversions;

/// <summary>
/// Conversores EF Core para value objects → string en BD.
/// Detalle técnico: EF compila las lambdas a expression trees, que NO permiten <c>out var</c>
/// ni <c>try/catch</c>. Por eso usamos métodos helper estáticos en lugar de parsing inline.
/// </summary>
public static class ValueConverters
{
    public static readonly ValueConverter<ProjectId, string> ProjectIdConverter = new(
        id => id.ToString(),
        s => ParseProjectId(s));

    public static readonly ValueConverter<EnvironmentId, string> EnvironmentIdConverter = new(
        id => id.ToString(),
        s => ParseEnvironmentId(s));

    public static readonly ValueConverter<ApplicationId, string> ApplicationIdConverter = new(
        id => id.ToString(),
        s => ParseApplicationId(s));

    public static readonly ValueConverter<EnvVarId, string> EnvVarIdConverter = new(
        id => id.ToString(),
        s => ParseEnvVarId(s));

    public static readonly ValueConverter<Slug, string> SlugConverter = new(
        s => s.Value,
        v => Slug.Create(v).Value);

    public static readonly ValueConverter<GitRepoUrl, string> GitRepoUrlConverter = new(
        g => g.Value,
        v => GitRepoUrl.Create(v).Value);

    public static readonly ValueConverter<ContainerName, string> ContainerNameConverter = new(
        c => c.Value,
        v => ContainerName.Create(v).Value);

    public static readonly ValueConverter<Port, int> PortConverter = new(
        p => p.Value,
        v => Port.Create(v).Value);

    private static ProjectId ParseProjectId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ProjectId(parsed.Value) : default;

    private static EnvironmentId ParseEnvironmentId(string s)
        => AethraId.TryParse(s, out var parsed) ? new EnvironmentId(parsed.Value) : default;

    private static ApplicationId ParseApplicationId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ApplicationId(parsed.Value) : default;

    private static EnvVarId ParseEnvVarId(string s)
        => AethraId.TryParse(s, out var parsed) ? new EnvVarId(parsed.Value) : default;
}
