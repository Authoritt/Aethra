using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Domain;

namespace Aethra.Modules.Cloudflare.Application.Mapping;

internal static class CloudflareMappers
{
    public static CloudflareZoneDto ToSummary(CloudflareZone zone, int recordsCount) => new(
        Id: zone.Id.ToString(),
        ExternalZoneId: zone.ZoneId,
        Name: zone.Name,
        Status: zone.Status.ToString(),
        AccountId: zone.AccountId,
        RecordsCount: recordsCount,
        CreatedAt: zone.CreatedAt,
        UpdatedAt: zone.UpdatedAt,
        LastSyncedAt: zone.LastSyncedAt);

    public static CloudflareZoneDetailDto ToDetail(CloudflareZone zone, IReadOnlyList<DnsRecord> records) => new(
        Id: zone.Id.ToString(),
        ExternalZoneId: zone.ZoneId,
        Name: zone.Name,
        Status: zone.Status.ToString(),
        AccountId: zone.AccountId,
        CreatedAt: zone.CreatedAt,
        UpdatedAt: zone.UpdatedAt,
        LastSyncedAt: zone.LastSyncedAt,
        Records: records.Select(ToDto).ToList());

    public static DnsRecordDto ToDto(DnsRecord r) => new(
        Id: r.Id.ToString(),
        ZoneId: r.ZoneId.ToString(),
        ExternalRecordId: r.ExternalRecordId,
        Type: r.Type.ToString(),
        Name: r.Name,
        Content: r.Content,
        Ttl: r.Ttl,
        Proxied: r.Proxied,
        Comment: r.Comment,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt,
        SyncedAt: r.SyncedAt,
        LastError: r.LastError);
}
