using System.Globalization;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;

/// <summary>
/// OT-008 T15: lee los DNS records de la zona DIRECTAMENTE de la API de Cloudflare (la verdad), no de la copia
/// local <c>db.DnsRecords</c>, que se desfasa (records borrados/creados a mano fuera de Aethra). Solo lectura:
/// no toca la copia local. Si la API falla devuelve error explícito (nunca cae a la caché).
/// </summary>
public sealed record ListLiveDnsRecordsQuery(string ZoneId) : IQuery<IReadOnlyList<CloudflareDnsRecordInfo>>;

internal sealed class ListLiveDnsRecordsHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec)
    : IQueryHandler<ListLiveDnsRecordsQuery, IReadOnlyList<CloudflareDnsRecordInfo>>
{
    public async Task<Result<IReadOnlyList<CloudflareDnsRecordInfo>>> Handle(
        ListLiveDnsRecordsQuery request, CancellationToken ct)
    {
        var idResult = IdParsing.ParseZoneId(request.ZoneId);
        if (idResult.IsFailure)
        {
            return idResult.Error;
        }
        var zoneId = idResult.Value;

        var zone = await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == zoneId, ct).ConfigureAwait(false);
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

        try
        {
            var live = await api.ListDnsRecordsAsync(zone.ZoneId, apiToken, ct).ConfigureAwait(false);
            return Result.Success<IReadOnlyList<CloudflareDnsRecordInfo>>(live);
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure(
                "cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"Cloudflare devolvio error (code {ex.Code}): {ex.Message}"));
        }
    }
}
