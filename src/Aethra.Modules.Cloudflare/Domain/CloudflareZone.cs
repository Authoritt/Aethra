using Aethra.Modules.Cloudflare.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Cloudflare.Domain;

/// <summary>
/// Zona DNS gestionada en Cloudflare. Aethra guarda el token API cifrado (con DataProtection,
/// purpose <c>aethra-cloudflare-token</c>) para realizar operaciones contra el API v4 sin
/// pedirlo de nuevo en cada llamada. La unica forma de extraer el token es decodificarlo
/// con el mismo data protector.
/// </summary>
public sealed class CloudflareZone : AggregateRoot<CloudflareZoneId>
{
    /// <summary>
    /// Identificador externo en Cloudflare (32 chars hex). Usado en todas las URL del API v4
    /// (<c>/zones/{zone_id}/...</c>). No confundir con <see cref="Entity{TId}.Id"/>.
    /// </summary>
    public string ZoneId { get; private set; }

    /// <summary>Nombre canonico de la zona, ej. <c>example.com</c>.</summary>
    public string Name { get; private set; }

    public CloudflareZoneStatus Status { get; private set; }

    /// <summary>Account id de Cloudflare al que pertenece la zona.</summary>
    public string AccountId { get; private set; }

    /// <summary>
    /// Token API cifrado con DataProtection. Nunca devolver crudo fuera del modulo.
    /// </summary>
    public byte[] ApiTokenCipher { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    private CloudflareZone(
        CloudflareZoneId id,
        string zoneId,
        string name,
        string accountId,
        byte[] apiTokenCipher,
        DateTimeOffset now) : base(id)
    {
        ZoneId = zoneId;
        Name = name;
        AccountId = accountId;
        ApiTokenCipher = apiTokenCipher;
        Status = CloudflareZoneStatus.Unknown;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static CloudflareZone Create(
        string zoneId,
        string name,
        string accountId,
        byte[] apiTokenCipher,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(apiTokenCipher);
        if (apiTokenCipher.Length == 0)
        {
            throw new ArgumentException("apiTokenCipher no puede estar vacio.", nameof(apiTokenCipher));
        }

        var zone = new CloudflareZone(
            CloudflareZoneId.New(),
            zoneId.Trim(),
            name.Trim().ToLowerInvariant(),
            accountId.Trim(),
            apiTokenCipher,
            now);
        zone.Raise(new CloudflareZoneRegisteredEvent(zone.Id, zone.ZoneId, zone.Name, zone.AccountId));
        return zone;
    }

    /// <summary>
    /// Reemplaza el token API cifrado. Util cuando el operador rota credenciales.
    /// </summary>
    public void UpdateToken(byte[] newCipher, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newCipher);
        if (newCipher.Length == 0)
        {
            throw new ArgumentException("newCipher no puede estar vacio.", nameof(newCipher));
        }
        ApiTokenCipher = newCipher;
        UpdatedAt = now;
        Raise(new CloudflareZoneTokenRotatedEvent(Id, ZoneId));
    }

    /// <summary>
    /// Actualiza el estado y nombre desde lo reportado por Cloudflare.
    /// </summary>
    public void UpdateFromSync(CloudflareZoneStatus status, string name, string accountId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        Status = status;
        Name = name.Trim().ToLowerInvariant();
        AccountId = accountId.Trim();
        UpdatedAt = now;
        LastSyncedAt = now;
    }

    /// <summary>
    /// Marca que la zona se acaba de sincronizar exitosamente. No cambia el estado.
    /// </summary>
    public void MarkSynced(DateTimeOffset now)
    {
        LastSyncedAt = now;
        UpdatedAt = now;
    }

    // EF Core
    private CloudflareZone() : base()
    {
        ZoneId = string.Empty;
        Name = string.Empty;
        AccountId = string.Empty;
        ApiTokenCipher = Array.Empty<byte>();
    }
}
