using System.Globalization;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;

/// <summary>
/// Elimina un DNS record en Cloudflare y la copia local. Si el record nunca fue sincronizado
/// (no tiene external_record_id) solo se borra el registro local.
/// </summary>
public sealed record DeleteDnsRecordCommand(string RecordId) : ICommand;

internal sealed class DeleteDnsRecordHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec,
    IClock clock) : ICommandHandler<DeleteDnsRecordCommand>
{
    public async Task<Result> Handle(DeleteDnsRecordCommand request, CancellationToken cancellationToken)
    {
        _ = clock;
        var idResult = IdParsing.ParseRecordId(request.RecordId);
        if (idResult.IsFailure)
        {
            return idResult.Error;
        }
        var record = await db.DnsRecords.FirstOrDefaultAsync(r => r.Id == idResult.Value, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound("cloudflare.record_not_found", $"Record '{request.RecordId}' no existe.");
        }

        if (!string.IsNullOrEmpty(record.ExternalRecordId))
        {
            var zone = await db.Zones.FirstOrDefaultAsync(z => z.Id == record.ZoneId, cancellationToken).ConfigureAwait(false);
            if (zone is null)
            {
                return Error.NotFound("cloudflare.zone_not_found", "Zona padre no existe.");
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
                await api.DeleteDnsRecordAsync(zone.ZoneId, record.ExternalRecordId!, apiToken, cancellationToken).ConfigureAwait(false);
            }
            catch (CloudflareApiException ex) when (ex.StatusCode == 404)
            {
                // Ya estaba borrado en Cloudflare — purgar localmente es consistente.
            }
            catch (CloudflareApiException ex)
            {
                return Error.Failure(
                    "cloudflare.api_error",
                    string.Create(CultureInfo.InvariantCulture, $"Cloudflare rechazo eliminar el record (code {ex.Code}): {ex.Message}"));
            }
        }

        record.MarkRemoved();
        db.DnsRecords.Remove(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
