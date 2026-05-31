using Aethra.Modules.Cloudflare.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Cloudflare.Domain;

/// <summary>
/// Un DNS record gestionado en Cloudflare. Aethra mantiene una copia local para evitar
/// listar el zone en cada operacion y para tener trazabilidad (created/updated/synced).
/// El record vive ligado a una <see cref="CloudflareZone"/> via <see cref="ZoneId"/>.
/// </summary>
public sealed class DnsRecord : AggregateRoot<DnsRecordId>
{
    /// <summary>Zona local a la que pertenece (FK).</summary>
    public CloudflareZoneId ZoneId { get; private set; }

    /// <summary>
    /// Id que Cloudflare devuelve cuando el record fue creado en su lado. Nulo hasta
    /// que <see cref="MarkSynced"/> registra la respuesta del API.
    /// </summary>
    public string? ExternalRecordId { get; private set; }

    public DnsRecordType Type { get; private set; }

    /// <summary>Nombre completo del record (FQDN). Cloudflare normaliza a lowercase.</summary>
    public string Name { get; private set; }

    /// <summary>IP, FQDN o texto segun el tipo. Cloudflare valida segun <see cref="Type"/>.</summary>
    public string Content { get; private set; }

    /// <summary>TTL en segundos. <c>1</c> significa "auto" en Cloudflare.</summary>
    public int Ttl { get; private set; }

    /// <summary>Indica si el trafico pasa por el proxy de Cloudflare (naranja).</summary>
    public bool Proxied { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SyncedAt { get; private set; }
    public string? LastError { get; private set; }

    private DnsRecord(
        DnsRecordId id,
        CloudflareZoneId zoneId,
        DnsRecordType type,
        string name,
        string content,
        int ttl,
        bool proxied,
        string? comment,
        DateTimeOffset now) : base(id)
    {
        ZoneId = zoneId;
        Type = type;
        Name = name;
        Content = content;
        Ttl = ttl;
        Proxied = proxied;
        Comment = comment;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static DnsRecord Create(
        CloudflareZoneId zoneId,
        DnsRecordType type,
        string name,
        string content,
        int ttl,
        bool proxied,
        string? comment,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (ttl < 1 || ttl > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL debe estar entre 1 y 86400 segundos.");
        }
        var record = new DnsRecord(
            DnsRecordId.New(),
            zoneId,
            type,
            name.Trim().ToLowerInvariant(),
            content.Trim(),
            ttl,
            proxied,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            now);
        return record;
    }

    /// <summary>
    /// Actualiza campos editables. Cualquier valor null deja el actual sin tocar.
    /// </summary>
    public void UpdateContent(string? content, int? ttl, bool? proxied, string? comment, DateTimeOffset now)
    {
        if (content is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(content);
            Content = content.Trim();
        }
        if (ttl is { } t)
        {
            if (t < 1 || t > 86400)
            {
                throw new ArgumentOutOfRangeException(nameof(ttl), "TTL debe estar entre 1 y 86400 segundos.");
            }
            Ttl = t;
        }
        if (proxied is { } p)
        {
            Proxied = p;
        }
        if (comment is not null)
        {
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        }
        UpdatedAt = now;
        Raise(new DnsRecordUpdatedEvent(Id, ZoneId, Type, Name));
    }

    /// <summary>
    /// Registra que el record existe en Cloudflare con el id externo entregado.
    /// </summary>
    public void MarkSynced(string externalRecordId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalRecordId);
        ExternalRecordId = externalRecordId.Trim();
        SyncedAt = now;
        UpdatedAt = now;
        LastError = null;
        Raise(new DnsRecordCreatedEvent(Id, ZoneId, ExternalRecordId, Type, Name));
    }

    public void MarkSyncFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        LastError = error;
    }

    public void MarkRemoved()
    {
        Raise(new DnsRecordDeletedEvent(Id, ZoneId, Name));
    }

    // EF Core
    private DnsRecord() : base()
    {
        Name = string.Empty;
        Content = string.Empty;
    }
}
