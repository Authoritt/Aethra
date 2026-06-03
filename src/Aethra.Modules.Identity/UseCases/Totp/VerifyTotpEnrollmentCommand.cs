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
/// F12.1B — segundo paso del enrollment: el user envia un codigo TOTP generado por su app
/// para confirmar que escaneo bien el QR. Si valida, activa 2FA y genera/devuelve los 10
/// recovery codes (UNICA vez que se muestran en plaintext).
/// </summary>
public sealed record VerifyTotpEnrollmentCommand(string UserId, string Code) : ICommand<TotpEnableResultDto>;

public sealed record TotpEnableResultDto(bool Enabled, IReadOnlyList<string> RecoveryCodes);

internal sealed class VerifyTotpEnrollmentHandler(
    IdentityDbContext db,
    ITotpSecretCodec codec,
    IClock clock)
    : ICommandHandler<VerifyTotpEnrollmentCommand, TotpEnableResultDto>
{
    public async Task<Result<TotpEnableResultDto>> Handle(VerifyTotpEnrollmentCommand request, CancellationToken ct)
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
        if (user.TotpEnabled)
        {
            return Error.Conflict("totp.already_enabled", "2FA ya esta activo.");
        }
        if (user.TotpSecretCipher is null || user.TotpSecretCipher.Length == 0)
        {
            return Error.Validation("totp.not_enrolled",
                "No hay enrollment en curso. Llama primero a /enroll.");
        }

        var secret = codec.Unprotect(user.TotpSecretCipher);
        if (!TotpGenerator.ValidateCode(secret, (request.Code ?? string.Empty).Trim()))
        {
            return Error.Validation("totp.invalid_code", "Codigo TOTP invalido.");
        }

        // Generamos 10 recovery codes en plaintext, los packeamos, ciframos y persistimos.
        var codes = RecoveryCodes.Generate();
        var packed = RecoveryCodes.Pack(codes);
        var packedCipher = codec.Protect(packed);

        user.CompleteTotpEnrollment(packedCipher, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TotpEnableResultDto(true, codes);
    }
}
