using System.Globalization;
using System.Reflection;
using Aethra.Modules.Services.Domain;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aethra.Modules.Services.Templates;

/// <summary>
/// Catálogo de <see cref="ServiceTemplate"/> disponibles para creación one-click.
/// El catálogo se carga una vez al arranque y se mantiene inmutable en memoria.
/// </summary>
public interface IServiceTemplateCatalog
{
    /// <summary>Devuelve todas las plantillas en orden estable (por <c>Id</c>).</summary>
    IReadOnlyList<ServiceTemplate> GetAll();

    /// <summary>Busca una plantilla por su id case-insensitive. <c>null</c> si no existe.</summary>
    ServiceTemplate? GetById(string id);
}

/// <summary>
/// Carga las plantillas desde los recursos embebidos <c>Templates\*.yaml</c> del assembly
/// <c>Aethra.Modules.Services</c>. Falla con <see cref="InvalidOperationException"/> si una
/// plantilla está malformada: queremos enterarnos al boot, no en runtime.
/// </summary>
internal sealed class EmbeddedServiceTemplateCatalog : IServiceTemplateCatalog
{
    private const string ResourcePrefix = "Aethra.Modules.Services.Templates.";
    private const string ResourceSuffix = ".yaml";

    private readonly List<ServiceTemplate> _templates;
    private readonly Dictionary<string, ServiceTemplate> _byId;

    public EmbeddedServiceTemplateCatalog(ILogger<EmbeddedServiceTemplateCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var assembly = typeof(EmbeddedServiceTemplateCatalog).Assembly;
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var loaded = new List<ServiceTemplate>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var template = LoadFromResource(assembly, resourceName, deserializer);
            loaded.Add(template);
        }

        // Orden estable por Id para que GetAll() sea determinista.
        loaded.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        _templates = loaded;

        var byId = new Dictionary<string, ServiceTemplate>(loaded.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var t in loaded)
        {
            if (!byId.TryAdd(t.Id, t))
            {
                throw new InvalidOperationException(
                    $"Plantilla duplicada con id '{t.Id}'. Ids deben ser únicos case-insensitive.");
            }
        }
        _byId = byId;

        logger.LogInformation("Catálogo de service templates cargado: {Count} plantilla(s) [{Ids}]",
            _templates.Count, string.Join(", ", _templates.Select(t => t.Id)));
    }

    public IReadOnlyList<ServiceTemplate> GetAll() => _templates;

    public ServiceTemplate? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        return _byId.TryGetValue(id, out var template) ? template : null;
    }

    private static ServiceTemplate LoadFromResource(Assembly assembly, string resourceName, IDeserializer deserializer)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Recurso embebido '{resourceName}' no se pudo abrir.");
        using var reader = new StreamReader(stream);

        ServiceTemplateYamlDto dto;
        try
        {
            dto = deserializer.Deserialize<ServiceTemplateYamlDto>(reader)
                ?? throw new InvalidOperationException(
                    $"Plantilla '{resourceName}' vacía o no parseable.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Plantilla '{resourceName}' malformada: {ex.Message}", ex);
        }

        return MapToTemplate(dto, resourceName);
    }

    private static ServiceTemplate MapToTemplate(ServiceTemplateYamlDto dto, string resourceName)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new InvalidOperationException($"Plantilla '{resourceName}': campo 'id' requerido.");
        }
        if (string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            throw new InvalidOperationException($"Plantilla '{resourceName}': campo 'display_name' requerido.");
        }
        if (string.IsNullOrWhiteSpace(dto.Type))
        {
            throw new InvalidOperationException($"Plantilla '{resourceName}': campo 'type' requerido.");
        }
        if (!Enum.TryParse<ServiceType>(dto.Type, ignoreCase: true, out var parsedType))
        {
            throw new InvalidOperationException(
                $"Plantilla '{resourceName}': 'type' = '{dto.Type}' no es un ServiceType válido.");
        }
        if (string.IsNullOrWhiteSpace(dto.Version))
        {
            throw new InvalidOperationException($"Plantilla '{resourceName}': campo 'version' requerido.");
        }
        if (string.IsNullOrWhiteSpace(dto.Image))
        {
            throw new InvalidOperationException($"Plantilla '{resourceName}': campo 'image' requerido.");
        }
        if (dto.InternalPort <= 0)
        {
            throw new InvalidOperationException(
                $"Plantilla '{resourceName}': 'internal_port' debe ser > 0 (era {dto.InternalPort.ToString(CultureInfo.InvariantCulture)}).");
        }
        if (string.IsNullOrWhiteSpace(dto.AdminUser))
        {
            throw new InvalidOperationException($"Plantilla '{resourceName}': campo 'admin_user' requerido.");
        }
        if (string.IsNullOrWhiteSpace(dto.AdminPasswordGenerator))
        {
            throw new InvalidOperationException(
                $"Plantilla '{resourceName}': campo 'admin_password_generator' requerido.");
        }

        var volumes = (dto.Volumes ?? [])
            .Select(v =>
            {
                if (string.IsNullOrWhiteSpace(v.Name))
                {
                    throw new InvalidOperationException(
                        $"Plantilla '{resourceName}': cada volume requiere 'name'.");
                }
                if (string.IsNullOrWhiteSpace(v.ContainerPath))
                {
                    throw new InvalidOperationException(
                        $"Plantilla '{resourceName}': volume '{v.Name}' requiere 'container_path'.");
                }
                return new TemplateVolume(v.Name, v.ContainerPath);
            })
            .ToArray();

        TemplateHealthcheck? hc = null;
        if (dto.Healthcheck is { } h)
        {
            if (h.Test is null || h.Test.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Plantilla '{resourceName}': healthcheck.test no puede ir vacío.");
            }
            if (h.IntervalSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"Plantilla '{resourceName}': healthcheck.interval_seconds debe ser > 0.");
            }
            if (h.Retries <= 0)
            {
                throw new InvalidOperationException(
                    $"Plantilla '{resourceName}': healthcheck.retries debe ser > 0.");
            }
            hc = new TemplateHealthcheck(h.Test, h.IntervalSeconds, h.Retries);
        }

        var env = dto.Env is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(dto.Env, StringComparer.Ordinal);

        // Categoría: si no la trae, intentamos derivarla del ServiceType para mantener compat
        // con templates legacy (postgres/redis/rabbit) que no la declaran.
        var category = string.IsNullOrWhiteSpace(dto.Category)
            ? DeriveCategory(parsedType)
            : dto.Category.Trim();

        var tags = (dto.Tags ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var dependencies = (dto.Dependencies ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .ToArray();

        return new ServiceTemplate(
            Id: dto.Id,
            DisplayName: dto.DisplayName,
            Type: parsedType,
            Version: dto.Version,
            Image: dto.Image,
            InternalPort: dto.InternalPort,
            ManagementPort: dto.ManagementPort,
            AdminUser: dto.AdminUser,
            AdminPasswordGenerator: dto.AdminPasswordGenerator,
            Env: env,
            Command: dto.Command,
            Volumes: volumes,
            Healthcheck: hc,
            Notes: dto.Notes,
            Category: category,
            Description: dto.Description,
            Tags: tags,
            IconUrl: dto.IconUrl,
            BindingSupported: dto.BindingSupported ?? DeriveBindingSupported(parsedType),
            Dependencies: dependencies,
            MultiContainer: dto.MultiContainer ?? false);
    }

    private static string DeriveCategory(ServiceType type) => type switch
    {
        ServiceType.Postgres => TemplateCategories.Database,
        ServiceType.MySQL => TemplateCategories.Database,
        ServiceType.MariaDB => TemplateCategories.Database,
        ServiceType.MongoDB => TemplateCategories.Database,
        ServiceType.ClickHouse => TemplateCategories.Database,
        ServiceType.Redis => TemplateCategories.Database,
        ServiceType.RabbitMQ => TemplateCategories.Messaging,
        ServiceType.Application => TemplateCategories.Other,
        _ => TemplateCategories.Other,
    };

    private static bool DeriveBindingSupported(ServiceType type) => type switch
    {
        ServiceType.Postgres or ServiceType.MySQL or ServiceType.MariaDB
            or ServiceType.MongoDB or ServiceType.Redis or ServiceType.RabbitMQ => true,
        _ => false,
    };

    // DTOs internos para YamlDotNet: classes con set-accessors públicos (records no se deserializan
    // limpiamente con el naming convention underscore en todas las versiones).
    private sealed class ServiceTemplateYamlDto
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        public int InternalPort { get; set; }
        public int? ManagementPort { get; set; }
        public string AdminUser { get; set; } = string.Empty;
        public string AdminPasswordGenerator { get; set; } = string.Empty;
        public Dictionary<string, string>? Env { get; set; }
        public List<string>? Command { get; set; }
        public List<VolumeYamlDto>? Volumes { get; set; }
        public HealthcheckYamlDto? Healthcheck { get; set; }
        public string? Notes { get; set; }
        // F12.2 metadata de catálogo (todo opcional para no romper plantillas legacy).
        public string? Category { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
        public string? IconUrl { get; set; }
        public bool? BindingSupported { get; set; }
        public List<string>? Dependencies { get; set; }
        public bool? MultiContainer { get; set; }
    }

    private sealed class VolumeYamlDto
    {
        public string Name { get; set; } = string.Empty;
        public string ContainerPath { get; set; } = string.Empty;
    }

    private sealed class HealthcheckYamlDto
    {
        public List<string>? Test { get; set; }
        public int IntervalSeconds { get; set; }
        public int Retries { get; set; }
    }
}
