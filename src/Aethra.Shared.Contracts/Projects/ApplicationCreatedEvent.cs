namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Una Application se ha creado. Otros módulos pueden suscribirse para:
/// - Monitoring: crear monitor uptime asociado.
/// - Proxy: registrar ruta si Hostname está definido.
/// - Notes: inicializar línea de tiempo.
/// </summary>
public sealed record ApplicationCreatedEvent(
    string ApplicationId,
    string EnvironmentId,
    string ProjectId,
    string Slug,
    string Name,
    string GitRepoUrl,
    string Branch,
    string? PrimaryHostname,
    string? TargetVmId
) : IntegrationEvent;
