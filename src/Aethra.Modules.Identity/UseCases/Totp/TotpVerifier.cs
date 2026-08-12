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
                    return await ConsumeAtomicallyAsync(user, i, db, clock, ct).ConfigureAwait(false);
                }
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Marca el código de recuperación como usado con un <b>UPDATE condicional</b>, y devuelve si
    /// esta petición fue la que lo consumió.
    ///
    /// <para>Un código de recuperación es de un solo uso: es el sustituto del segundo factor cuando
    /// el usuario pierde el dispositivo. La versión anterior leía la máscara, buscaba el bit libre,
    /// lo marcaba en memoria y guardaba — un <i>leer, modificar, escribir</i> sin control de
    /// concurrencia. Dos intentos de login simultáneos con el MISMO código leían la misma máscara,
    /// los dos veían el bit libre y los dos entraban. Un código robado se puede reutilizar
    /// tantas veces como peticiones se lancen a la vez.</para>
    ///
    /// <para>La condición <c>(mask &amp; bit) = 0</c> viaja EN el <c>UPDATE</c>, así que la base
    /// resuelve la carrera: el primero cambia una fila, el resto cambia cero. No hace falta ni token
    /// de concurrencia ni transacción serializable — el propio <c>UPDATE</c> es el punto de
    /// serialización, y el número de filas afectadas es la respuesta.</para>
    /// </summary>
    private static async Task<bool> ConsumeAtomicallyAsync(
        User user, int index, IdentityDbContext db, IClock clock, CancellationToken ct)
    {
        var bit = 1 << index;
        var now = clock.UtcNow;
        var userId = user.Id;

        var affected = await db.Users
            .Where(u => u.Id == userId && (u.TotpRecoveryCodesUsedMask & bit) == 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.TotpRecoveryCodesUsedMask, u => u.TotpRecoveryCodesUsedMask | bit)
                    .SetProperty(u => u.UpdatedAt, now),
                ct)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            // Otra petición se lo llevó entre la lectura y esta escritura. Rechazar es lo correcto:
            // el código ya no está disponible, y quien lo consumió fue la otra.
            return false;
        }

        // La entidad cargada quedó con la máscara vieja: hay que sincronizarla para que un
        // SaveChanges posterior en la misma petición no la reescriba y "des-consuma" el código.
        //
        // Se hace EN MEMORIA, sin volver a consultar. Un ReloadAsync aquí sería una segunda
        // operación que puede fallar (cancelación, error transitorio) DESPUÉS de que el UPDATE ya
        // se haya confirmado solo: el código quedaría consumido para siempre y el catch de arriba
        // convertiría esa excepción en "código inválido", dejando fuera a un usuario que acaba de
        // gastar —quizá— su último código de recuperación. Pasado el punto de no retorno, nada que
        // pueda fallar debe decidir si el login se acepta.
        var entry = db.Entry(user);
        SyncTracked(entry.Property(u => u.TotpRecoveryCodesUsedMask), user.TotpRecoveryCodesUsedMask | bit);
        SyncTracked(entry.Property(u => u.UpdatedAt), now);
        return true;

        // Se ajustan CurrentValue y OriginalValue a la vez: así el valor queda al día y además la
        // propiedad NO se marca como modificada, que es lo que evita que un SaveChanges posterior
        // vuelva a escribirla.
        static void SyncTracked<T>(
            Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<User, T> property, T value)
        {
            property.CurrentValue = value;
            property.OriginalValue = value;
            property.IsModified = false;
        }
    }
}
