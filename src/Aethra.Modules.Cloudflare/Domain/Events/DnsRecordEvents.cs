using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Cloudflare.Domain.Events;

/// <summary>
/// Un DNS record fue creado en Cloudflare y persistido localmente.
/// </summary>
public sealed record DnsRecordCreatedEvent(
    DnsRecordId RecordId,
    CloudflareZoneId ZoneId,
    string ExternalRecordId,
    DnsRecordType Type,
    string Name) : DomainEvent;

/// <summary>
/// Un DNS record fue actualizado (contenido/ttl/proxied/comment).
/// </summary>
public sealed record DnsRecordUpdatedEvent(
    DnsRecordId RecordId,
    CloudflareZoneId ZoneId,
    DnsRecordType Type,
    string Name) : DomainEvent;

/// <summary>
/// Un DNS record fue eliminado de Cloudflare y debe purgarse del store local.
/// </summary>
public sealed record DnsRecordDeletedEvent(
    DnsRecordId RecordId,
    CloudflareZoneId ZoneId,
    string Name) : DomainEvent;
