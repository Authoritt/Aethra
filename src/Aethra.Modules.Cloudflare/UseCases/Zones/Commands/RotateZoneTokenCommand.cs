using System.Globalization;
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
/// Rota el token API de una zona ya registrada. Verifica el nuevo token contra el API antes
/// de persistir el cipher actualizado para evitar dejar la zona en estado invalido.
/// </summary>
public sealed record RotateZoneTokenCommand(string ZoneId, string NewApiToken) : ICommand;

public sealed class RotateZoneTokenValidator : AbstractValidator<RotateZoneTokenCommand>
{
    public RotateZoneTokenValidator()
    {
        RuleFor(c => c.ZoneId).NotEmpty();
        RuleFor(c => c.NewApiToken).NotEmpty().MinimumLength(8);
    }
}

internal sealed class RotateZoneTokenHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec,
    IClock clock) : ICommandHandler<RotateZoneTokenCommand>
{
    public async Task<Result> Handle(RotateZoneTokenCommand request, CancellationToken cancellationToken)
    {
        var idResult = IdParsing.ParseZoneId(request.ZoneId);
        if (idResult.IsFailure)
        {
            return idResult.Error;
        }
        var zone = await db.Zones.FirstOrDefaultAsync(z => z.Id == idResult.Value, cancellationToken).ConfigureAwait(false);
        if (zone is null)
        {
            return Error.NotFound("cloudflare.zone_not_found", $"Zona '{request.ZoneId}' no existe.");
        }

        try
        {
            _ = await api.GetZoneAsync(zone.ZoneId, request.NewApiToken, cancellationToken).ConfigureAwait(false);
        }
        catch (CloudflareApiException ex)
        {
            return Error.Validation(
                "cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"Cloudflare rechazo el token nuevo (code {ex.Code}): {ex.Message}"));
        }

        var cipher = codec.Encode(request.NewApiToken);
        zone.UpdateToken(cipher, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
