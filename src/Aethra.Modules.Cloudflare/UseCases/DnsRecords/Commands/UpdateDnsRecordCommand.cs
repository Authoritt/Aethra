using System.Globalization;
using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Application.Mapping;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;

/// <summary>
/// Actualiza campos editables (<c>content</c>, <c>ttl</c>, <c>proxied</c>, <c>comment</c>)
/// de un DNS record. Si todos llegan en null no se hace llamada al API.
/// </summary>
public sealed record UpdateDnsRecordCommand(
    string RecordId,
    string? Content,
    int? Ttl,
    bool? Proxied,
    string? Comment) : ICommand<DnsRecordDto>;

internal sealed class UpdateDnsRecordHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec,
    IClock clock) : ICommandHandler<UpdateDnsRecordCommand, DnsRecordDto>
{
    public async Task<Result<DnsRecordDto>> Handle(UpdateDnsRecordCommand request, CancellationToken cancellationToken)
    {
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
        if (string.IsNullOrEmpty(record.ExternalRecordId))
        {
            return Error.Conflict("cloudflare.record_not_synced",
                "El record aun no esta sincronizado con Cloudflare; no se puede actualizar.");
        }

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
            record.UpdateContent(request.Content, request.Ttl, request.Proxied, request.Comment, clock.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return Error.Validation("cloudflare.invalid_record", ex.Message);
        }

        try
        {
            await api.UpdateDnsRecordAsync(
                zone.ZoneId,
                record.ExternalRecordId!,
                apiToken,
                new UpdateDnsRecordRequest(
                    record.Type.ToString(),
                    record.Name,
                    record.Content,
                    record.Ttl,
                    record.Proxied,
                    record.Comment),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure(
                "cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"Cloudflare rechazo actualizar el record (code {ex.Code}): {ex.Message}"));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CloudflareMappers.ToDto(record);
    }
}
