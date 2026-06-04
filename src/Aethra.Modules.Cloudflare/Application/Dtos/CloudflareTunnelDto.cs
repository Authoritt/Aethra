namespace Aethra.Modules.Cloudflare.Application.Dtos;

/// <summary>F13.9 — vista de un Cloudflare Tunnel gestionado remotamente por Aethra (sin token).</summary>
public sealed record CloudflareTunnelDto(
    string Id,
    string TunnelId,
    string Name,
    string AccountId,
    string AethraService,
    string FallbackService,
    bool FallbackNoTlsVerify,
    string? TargetVmId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSyncedAt,
    IReadOnlyList<TunnelIngressRuleDto> Ingress);

/// <summary>Una regla de ingress del túnel (hostname null = catch-all).</summary>
public sealed record TunnelIngressRuleDto(string? Hostname, string Service, bool NoTlsVerify);
