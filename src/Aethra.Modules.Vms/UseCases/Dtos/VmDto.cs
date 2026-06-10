namespace Aethra.Modules.Vms.UseCases.Dtos;

public sealed record VmDto(
    string Id,
    string Slug,
    string Name,
    string? PublicIp,
    string? PrivateIp,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    string? Hostname,
    string? KernelVersion,
    string? CpuModel,
    int? CpuCores,
    long? TotalMemoryBytes,
    string? AgentVersion,
    bool AcceptsPreviews = true,
    string? ContainerRuntime = null,
    long? RootDiskTotalBytes = null,
    long? RootDiskAvailableBytes = null);
