using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Identity.UseCases.Totp;

/// <summary>
/// F12.1B — comienza el enrollment 2FA: genera un secret, lo persiste cifrado, y devuelve
/// la URI otpauth + secret base32 para que el frontend muestre el QR. El secret no queda
/// activo hasta que el usuario verifique un codigo via <see cref="VerifyTotpEnrollmentCommand"/>.
/// </summary>
public sealed record EnrollTotpCommand(string UserId) : ICommand<TotpEnrollResultDto>;

public sealed record TotpEnrollResultDto(string OtpAuthUri, string SecretBase32, string Issuer, string Account);

internal sealed class EnrollTotpHandler(
    IdentityDbContext db,
    ITotpSecretCodec codec,
    IOptions<IdentityOptions> options,
    IClock clock)
    : ICommandHandler<EnrollTotpCommand, TotpEnrollResultDto>
{
    public async Task<Result<TotpEnrollResultDto>> Handle(EnrollTotpCommand request, CancellationToken ct)
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
            return Error.Conflict("totp.already_enabled",
                "2FA ya esta activo. Para regenerar el secret primero desactiva 2FA.");
        }

        var secret = TotpGenerator.GenerateSecret();
        var cipher = codec.Protect(secret);

        user.BeginTotpEnrollment(cipher, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var issuer = options.Value.TotpIssuer;
        var account = user.Email;
        var uri = TotpGenerator.BuildOtpAuthUri(issuer, account, secret);
        var b32 = TotpGenerator.ToBase32(secret);

        return new TotpEnrollResultDto(uri, b32, issuer, account);
    }
}
