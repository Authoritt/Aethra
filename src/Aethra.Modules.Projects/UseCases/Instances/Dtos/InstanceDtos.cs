namespace Aethra.Modules.Projects.UseCases.Instances.Dtos;

/// <summary>
/// Vista de listado de una <c>Instance</c>. La UI consume esto para mostrar las instancias de
/// un Template con su environment, hostname efectivo y VM target.
/// </summary>
public sealed record InstanceSummary(
    string id,
    string templateId,
    string clientId,
    string clientSlug,
    string environment,
    string slug,
    string targetVmId,
    string containerName,
    bool autoDeployOnNewBuild,
    string? customDomain,
    string? autoHostname,
    int? primaryPort,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt,
    string? trackedRef = null,
    string? effectiveTrackedRef = null,
    bool isEphemeral = false,
    string? createdByUserId = null);

/// <summary>
/// Detalle de una <c>Instance</c>. Igual que summary; reservado para campos derivados (estado
/// last deploy, healthcheck status) sin romper el contrato cuando se cableen.
/// </summary>
public sealed record InstanceDetail(
    string id,
    string templateId,
    string clientId,
    string clientSlug,
    string environment,
    string slug,
    string targetVmId,
    string containerName,
    bool autoDeployOnNewBuild,
    string? customDomain,
    string? autoHostname,
    IReadOnlyList<InstancePortDto> ports,
    IReadOnlyList<InstanceVolumeDto> volumes,
    InstanceHealthcheckDto? healthcheck,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt,
    string? trackedRef = null,
    string? effectiveTrackedRef = null,
    bool isEphemeral = false,
    DateTimeOffset? expiresAt = null,
    string? createdByUserId = null,
    // OT-009 — hostname por servicio fijado en esta instancia (override del template).
    IReadOnlyList<InstanceServiceHostnameDto>? serviceHostnames = null);

/// <summary>OT-009 — override de hostname de un servicio en una instancia.</summary>
public sealed record InstanceServiceHostnameDto(string serviceName, string hostname);

public sealed record InstancePortDto(int containerPort, int? hostPort, string protocol);

public sealed record InstanceVolumeDto(string name, string containerPath, bool readOnly);

public sealed record InstanceHealthcheckDto(
    IReadOnlyList<string> test,
    int intervalSeconds,
    int retries,
    int? timeoutSeconds,
    int? startPeriodSeconds);
