namespace Aethra.Modules.Services.UseCases.Dtos;

public sealed record ManagedServiceSummaryDto(
    string Id,
    string Slug,
    string Name,
    string Type,
    string Version,
    string Status,
    string TargetVmId,
    string ContainerName,
    int BindingsCount);

public sealed record ManagedServiceDetailDto(
    string Id,
    string Slug,
    string Name,
    string Type,
    string Version,
    string Status,
    string TargetVmId,
    string ContainerName,
    string Image,
    int InternalPort,
    string NetworkName,
    bool ExposedExternally,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProvisionedAt,
    string? ErrorCode,
    string? ErrorMessage,
    int BindingsCount);

public sealed record ServiceTemplateDto(
    string Id,
    string DisplayName,
    string Type,
    string Version,
    string Image,
    int InternalPort,
    string? Notes,
    string Category,
    string? Description,
    IReadOnlyList<string> Tags,
    string? IconUrl,
    bool BindingSupported,
    IReadOnlyList<string> Dependencies,
    bool MultiContainer);

public sealed record ServiceBindingDto(
    string Id,
    string ServiceId,
    string InstanceId,
    string? InstanceSlug,
    string ResourceName,
    string Permissions,
    string EnvVarPrefix,
    bool HasMigrationsHook,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProvisionedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastRotatedAt);

public sealed record ServiceBackupDto(
    string Id,
    string ServiceId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Status,
    long? SizeBytes,
    string DestinationPath,
    string? ErrorMessage);

public sealed record BackupPolicyDto(
    string CronExpression,
    int RetentionCount,
    string Destination);
