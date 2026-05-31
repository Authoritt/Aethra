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
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;

/// <summary>
/// Crea un DNS record en Cloudflare y persiste la copia local con el id externo.
/// </summary>
public sealed record CreateDnsRecordCommand(
    string ZoneId,
    string Type,
    string Name,
    string Content,
    int Ttl,
    bool Proxied,
    string? Comment) : ICommand<DnsRecordDto>;

public sealed class CreateDnsRecordValidator : AbstractValidator<CreateDnsRecordCommand>
{
    private static readonly string[] AllowedTypes = ["A", "AAAA", "CNAME", "TXT", "MX"];

    public CreateDnsRecordValidator()
    {
        RuleFor(c => c.ZoneId).NotEmpty();
        RuleFor(c => c.Type)
            .NotEmpty()
            .Must(t => AllowedTypes.Contains(t.ToUpperInvariant()))
            .WithMessage("Tipo invalido. Use A | AAAA | CNAME | TXT | MX.");
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(255)
            .Matches("^(?=.{1,253}$)(\\*\\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$")
            .WithMessage("El nombre debe ser un FQDN valido.");
        RuleFor(c => c.Content).NotEmpty().MaximumLength(2048);
        RuleFor(c => c.Ttl).InclusiveBetween(1, 86400);
        RuleFor(c => c.Comment).MaximumLength(1024).When(c => c.Comment is not null);
    }
}

internal sealed class CreateDnsRecordHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec,
    IClock clock) : ICommandHandler<CreateDnsRecordCommand, DnsRecordDto>
{
    public async Task<Result<DnsRecordDto>> Handle(CreateDnsRecordCommand request, CancellationToken cancellationToken)
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
        if (!Enum.TryParse<DnsRecordType>(request.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("cloudflare.invalid_type", "Tipo invalido.");
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

        var now = clock.UtcNow;
        DnsRecord record;
        try
        {
            record = DnsRecord.Create(zoneId, type, request.Name, request.Content, request.Ttl, request.Proxied,
                request.Comment, now);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return Error.Validation("cloudflare.invalid_record", ex.Message);
        }

        string externalId;
        try
        {
            externalId = await api.CreateDnsRecordAsync(
                zone.ZoneId,
                apiToken,
                new CreateDnsRecordRequest(
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
                string.Create(CultureInfo.InvariantCulture, $"Cloudflare rechazo crear el record (code {ex.Code}): {ex.Message}"));
        }

        record.MarkSynced(externalId, now);
        db.DnsRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CloudflareMappers.ToDto(record);
    }
}
