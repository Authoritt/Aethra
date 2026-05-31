using Aethra.Modules.Projects.Domain;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Projects.Infrastructure.Conversions;

/// <summary>
/// Conversores EF Core para los value objects que sobreviven al refactor F9.0.
/// Los converters para los nuevos IDs (Template/Client/Instance) los aporta A1 dentro de sus
/// respectivas subcarpetas si los necesita en la sub-fase de persistence.
///
/// Detalle técnico: EF compila las lambdas a expression trees, que NO permiten <c>out var</c>
/// ni <c>try/catch</c>. Por eso usamos métodos helper estáticos en lugar de parsing inline.
/// </summary>
public static class ValueConverters
{
    public static readonly ValueConverter<ProjectId, string> ProjectIdConverter = new(
        id => id.ToString(),
        s => ParseProjectId(s));

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

    private static EnvVarId ParseEnvVarId(string s)
        => AethraId.TryParse(s, out var parsed) ? new EnvVarId(parsed.Value) : default;
}
