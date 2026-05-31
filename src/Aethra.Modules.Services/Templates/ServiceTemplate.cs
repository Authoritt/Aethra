using Aethra.Modules.Services.Domain;

namespace Aethra.Modules.Services.Templates;

/// <summary>
/// Plantilla declarativa one-click para un tipo de servicio gestionado.
/// Se carga desde un YAML embebido (ver <see cref="EmbeddedServiceTemplateCatalog"/>)
/// y describe imagen, puertos, env, comando, volúmenes y healthcheck que el orchestrator
/// usará al provisionar el contenedor.
/// </summary>
public sealed record ServiceTemplate(
    string Id,
    string DisplayName,
    ServiceType Type,
    string Version,
    string Image,
    int InternalPort,
    int? ManagementPort,
    string AdminUser,
    string AdminPasswordGenerator,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<string>? Command,
    IReadOnlyList<TemplateVolume> Volumes,
    TemplateHealthcheck? Healthcheck,
    string? Notes);

/// <summary>
/// Volumen lógico montado por la plantilla. El orchestrator mapea <paramref name="Name"/>
/// a un volumen Docker concreto cuando crea la instancia.
/// </summary>
public sealed record TemplateVolume(string Name, string ContainerPath);

/// <summary>
/// Healthcheck Docker derivado del HEALTHCHECK CLI. <paramref name="Test"/> sigue el formato
/// estándar de compose: <c>["CMD", ...]</c> o <c>["CMD-SHELL", "..."]</c>.
/// </summary>
public sealed record TemplateHealthcheck(
    IReadOnlyList<string> Test,
    int IntervalSeconds,
    int Retries);
