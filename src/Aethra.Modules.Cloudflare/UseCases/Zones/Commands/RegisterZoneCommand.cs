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

namespace Aethra.Modules.Cloudflare.UseCases.Zones.Commands;

/// <summary>
/// Registra una zona Cloudflare en Aethra. El handler verifica el token contra el API real
/// (<c>GET /zones/{id}</c>), guarda el token cifrado y persiste la zona con la metadata
/// devuelta por Cloudflare.
/// </summary>
public sealed record RegisterZoneCommand(string ZoneId, string ApiToken) : ICommand<CloudflareZoneDto>;

public sealed class RegisterZoneValidator : AbstractValidator<RegisterZoneCommand>
{
    // Cloudflare zone ids son 32 chars hex.
    public RegisterZoneValidator()
    {
        RuleFor(c => c.ZoneId)
            .NotEmpty()
            .Matches("^[0-9a-fA-F]{32}$")
            .WithMessage("El zone_id debe ser una cadena hex de 32 caracteres.");
        RuleFor(c => c.ApiToken).NotEmpty().MinimumLength(8);
    }
}

internal sealed class RegisterZoneHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec,
    IClock clock) : ICommandHandler<RegisterZoneCommand, CloudflareZoneDto>
{
    public async Task<Result<CloudflareZoneDto>> Handle(RegisterZoneCommand request, CancellationToken cancellationToken)
    {
        var externalId = request.ZoneId.Trim().ToLowerInvariant();
        if (await db.Zones.AnyAsync(z => z.ZoneId == externalId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("cloudflare.zone_already_registered",
                $"La zona '{externalId}' ya esta registrada en Aethra.");
        }

        CloudflareZoneInfo info;
        try
        {
            info = await api.GetZoneAsync(externalId, request.ApiToken, cancellationToken).ConfigureAwait(false);
        }
        catch (CloudflareApiException ex)
        {
            return Error.Validation(
                "cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"Cloudflare rechazo la zona (code {ex.Code}): {ex.Message}"));
        }

        var cipher = codec.Encode(request.ApiToken);
        var now = clock.UtcNow;
        var zone = CloudflareZone.Create(info.Id, info.Name, info.AccountId, cipher, now);
        zone.UpdateFromSync(MapStatus(info.Status), info.Name, info.AccountId, now);

        db.Zones.Add(zone);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return CloudflareMappers.ToSummary(zone, recordsCount: 0);
    }

    internal static CloudflareZoneStatus MapStatus(string? raw)
        => raw?.ToLowerInvariant() switch
        {
            "active" => CloudflareZoneStatus.Active,
            "pending" => CloudflareZoneStatus.Pending,
            "suspended" => CloudflareZoneStatus.Suspended,
            _ => CloudflareZoneStatus.Unknown,
        };
}
