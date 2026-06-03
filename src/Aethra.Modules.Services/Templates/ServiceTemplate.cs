using Aethra.Modules.Services.Domain;

namespace Aethra.Modules.Services.Templates;

/// <summary>
/// Plantilla declarativa one-click para un tipo de servicio gestionado.
/// Se carga desde un YAML embebido (ver <see cref="EmbeddedServiceTemplateCatalog"/>)
/// y describe imagen, puertos, env, comando, volúmenes y healthcheck que el orchestrator
/// usará al provisionar el contenedor.
/// </summary>
/// <remarks>
/// F12.2 añade metadata de catálogo (categoría, tags, iconUrl, dependencies) que la UI
/// usa para renderizar el marketplace. Estos campos son opcionales para no romper las
/// plantillas legacy (postgres-16/redis-7/rabbitmq-3-mgmt).
/// </remarks>
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
    string? Notes,
    string Category,
    string? Description,
    IReadOnlyList<string> Tags,
    string? IconUrl,
    bool BindingSupported,
    IReadOnlyList<string> Dependencies,
    bool MultiContainer);

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

/// <summary>
/// Categorías canónicas usadas por la UI del marketplace para los chips de filtro.
/// </summary>
public static class TemplateCategories
{
    public const string Database = "Database";
    public const string Messaging = "Messaging";
    public const string Storage = "Storage";
    public const string Cms = "CMS";
    public const string Analytics = "Analytics";
    public const string Automation = "Automation";
    public const string Search = "Search";
    public const string Other = "Other";
}
