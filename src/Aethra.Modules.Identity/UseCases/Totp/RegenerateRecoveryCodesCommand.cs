using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.UseCases.Totp;

/// <summary>
/// F12.1B — regenera los 10 recovery codes. Requiere code TOTP valido. Los codigos previos
/// quedan inutilizables (bitmask se resetea pero el set entero cambia tambien).
/// </summary>
public sealed record RegenerateRecoveryCodesCommand(string UserId, string Code)
    : ICommand<RegenerateRecoveryCodesResultDto>;

public sealed record RegenerateRecoveryCodesResultDto(IReadOnlyList<string> RecoveryCodes);

internal sealed class RegenerateRecoveryCodesHandler(
    IdentityDbContext db,
    ITotpSecretCodec codec,
    IClock clock)
    : ICommandHandler<RegenerateRecoveryCodesCommand, RegenerateRecoveryCodesResultDto>
{
    public async Task<Result<RegenerateRecoveryCodesResultDto>> Handle(
        RegenerateRecoveryCodesCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.UserId, out var parsed) || parsed.Value.Prefix != "usr")
        {
            return Error.Validation("user.invalid_id", $"UserId invalido: '{request.UserId}'.");
        }
        var uid = new UserId(parsed.Value);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("user.not_found", $"User '{request.UserId}' no existe.");
        }
        if (!user.TotpEnabled)
        {
            return Error.Conflict("totp.not_enabled", "2FA no esta activo.");
        }
        var verified = await TotpVerifier.VerifyAsync(user, request.Code, codec, db, clock, ct)
            .ConfigureAwait(false);
        if (!verified)
        {
            return Error.Validation("totp.invalid_code", "Codigo TOTP/recovery invalido.");
        }

        var codes = RecoveryCodes.Generate();
        var packed = RecoveryCodes.Pack(codes);
        var cipher = codec.Protect(packed);
        user.RotateRecoveryCodes(cipher, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new RegenerateRecoveryCodesResultDto(codes);
    }
}
