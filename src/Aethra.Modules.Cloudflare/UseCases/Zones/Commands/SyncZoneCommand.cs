using System.Globalization;
using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Application.Mapping;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.Zones.Commands;

/// <summary>
/// Sincroniza el estado de la zona y la lista de DNS records contra Cloudflare. Hace
/// upsert por <c>external_record_id</c>: crea registros nuevos, actualiza los existentes y
/// elimina los locales que ya no estan en Cloudflare.
/// </summary>
public sealed record SyncZoneCommand(string ZoneId) : ICommand<CloudflareZoneDetailDto>;

internal sealed class SyncZoneHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec,
    IClock clock) : ICommandHandler<SyncZoneCommand, CloudflareZoneDetailDto>
{
    public async Task<Result<CloudflareZoneDetailDto>> Handle(SyncZoneCommand request, CancellationToken cancellationToken)
    {
        var idResult = IdParsing.ParseZoneId(request.ZoneId);
        if (idResult.IsFailure)
        {
            return idResult.Error;
        }
        var zoneId = idResult.Value;

        var zone = await db.Zones.FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken).ConfigureAwait(false);
        if (zone is null)
        {
            return Error.NotFound("cloudflare.zone_not_found", $"Zona '{request.ZoneId}' no existe.");
        }

        string apiToken;
        try
        {
            apiToken = codec.Decode(zone.ApiTokenCipher);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            return Error.Failure("cloudflare.token_decrypt_failed", "No se pudo descifrar el token de la zona.");
        }

        CloudflareZoneInfo zoneInfo;
        IReadOnlyList<CloudflareDnsRecordInfo> remote;
        try
        {
            zoneInfo = await api.GetZoneAsync(zone.ZoneId, apiToken, cancellationToken).ConfigureAwait(false);
            remote = await api.ListDnsRecordsAsync(zone.ZoneId, apiToken, cancellationToken).ConfigureAwait(false);
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure(
                "cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"Cloudflare devolvio error (code {ex.Code}): {ex.Message}"));
        }

        var now = clock.UtcNow;
        zone.UpdateFromSync(RegisterZoneHandler.MapStatus(zoneInfo.Status), zoneInfo.Name, zoneInfo.AccountId, now);

        var local = await db.DnsRecords.Where(r => r.ZoneId == zoneId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var localByExternal = local
            .Where(r => !string.IsNullOrEmpty(r.ExternalRecordId))
            .ToDictionary(r => r.ExternalRecordId!, StringComparer.OrdinalIgnoreCase);

        var seenExternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in remote)
        {
            if (!TryMapType(r.Type, out var typed))
            {
                // Skipea tipos no soportados (NS, SRV, CAA, etc.). El operador puede gestionarlos
                // por la consola de Cloudflare hasta que el modulo los soporte.
                continue;
            }
            seenExternal.Add(r.Id);
            if (localByExternal.TryGetValue(r.Id, out var existing))
            {
                existing.UpdateContent(r.Content, r.Ttl, r.Proxied, r.Comment, now);
                existing.MarkSynced(r.Id, now);
            }
            else
            {
                var created = DnsRecord.Create(zoneId, typed, r.Name, r.Content, r.Ttl, r.Proxied, r.Comment, now);
                created.MarkSynced(r.Id, now);
                db.DnsRecords.Add(created);
            }
        }

        // Cualquier record local cuyo external_id ya no esta en Cloudflare ha sido removido alla.
        foreach (var orphan in local.Where(r => r.ExternalRecordId is not null && !seenExternal.Contains(r.ExternalRecordId)))
        {
            orphan.MarkRemoved();
            db.DnsRecords.Remove(orphan);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var refreshed = await db.DnsRecords
            .AsNoTracking()
            .Where(r => r.ZoneId == zoneId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return CloudflareMappers.ToDetail(zone, refreshed);
    }

    private static bool TryMapType(string raw, out DnsRecordType type)
        => Enum.TryParse(raw, ignoreCase: true, out type);
}
