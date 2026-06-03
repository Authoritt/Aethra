using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.UseCases.Totp;

/// <summary>
/// F12.1B — helper para validar un codigo entrante. Acepta:
/// <list type="bullet">
///   <item>6 digitos numericos → valida como TOTP RFC 6238.</item>
///   <item>8 chars alfanumericos (formato recovery) → valida contra los 10 cifrados; si OK,
///   marca el bit usado en el bitmask del user (one-shot).</item>
/// </list>
/// El metodo NO hace SaveChangesAsync — el caller decide cuando persistir (ej. en login flow
/// el codigo verificado puede invalidar otros estados).
/// </summary>
internal static class TotpVerifier
{
    public static async Task<bool> VerifyAsync(
        User user, string? code, ITotpSecretCodec codec,
        IdentityDbContext db, IClock clock, CancellationToken ct)
    {
        if (!user.TotpEnabled || user.TotpSecretCipher is null) { return false; }
        if (string.IsNullOrWhiteSpace(code)) { return false; }
        var trimmed = code.Trim();

        if (RecoveryCodes.LooksLikeRecoveryCode(trimmed))
        {
            return await VerifyRecoveryAsync(user, trimmed, codec, db, clock, ct).ConfigureAwait(false);
        }
        // TOTP 6 digitos.
        return VerifyTotp(user, trimmed, codec);
    }

    private static bool VerifyTotp(User user, string code, ITotpSecretCodec codec)
    {
        if (user.TotpSecretCipher is null) { return false; }
        if (code.Length != 6) { return false; }
        foreach (var ch in code)
        {
            if (!char.IsDigit(ch)) { return false; }
        }
        try
        {
            var secret = codec.Unprotect(user.TotpSecretCipher);
            return TotpGenerator.ValidateCode(secret, code);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> VerifyRecoveryAsync(
        User user, string code, ITotpSecretCodec codec,
        IdentityDbContext db, IClock clock, CancellationToken ct)
    {
        if (user.TotpRecoveryCodesCipher is null) { return false; }
        try
        {
            var packed = codec.Unprotect(user.TotpRecoveryCodesCipher);
            var codes = RecoveryCodes.Unpack(packed);
            var normalized = RecoveryCodes.Normalize(code);
            for (var i = 0; i < codes.Count; i++)
            {
                if (RecoveryCodes.IsUsed(user.TotpRecoveryCodesUsedMask, i)) { continue; }
                if (string.Equals(codes[i], normalized, StringComparison.Ordinal))
                {
                    user.ConsumeRecoveryCode(i, clock.UtcNow);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return true;
                }
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
