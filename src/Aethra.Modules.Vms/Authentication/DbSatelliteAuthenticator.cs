using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.Authentication;

/// <summary>
/// Verifica el token del satélite contra la BD. Como los tokens son de 256 bits de entropía,
/// usamos un salt determinístico en Argon2id (ver <see cref="SatelliteToken.HashOnly"/>) para
/// poder hacer lookup por hash en O(log n) con índice en lugar de scan.
/// </summary>
internal sealed class DbSatelliteAuthenticator(VmsDbContext db) : ISatelliteAuthenticator
{
    public async Task<VmId?> AuthenticateAsync(string presentedToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return null;
        }

        var hash = SatelliteToken.HashOnly(presentedToken);

        // EF Owned types: el shadow path es "Satellite.Token.Hash".
        var vm = await db.Vms
            .Where(v => v.Satellite.Token.Hash == hash)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return vm?.Id;
    }
}
