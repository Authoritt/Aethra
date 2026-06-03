using Aethra.Shared.Contracts.Vms;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.Infrastructure.Lookups;

/// <summary>
/// F12.3 — Implementación EF de <see cref="IPreviewVmPool"/>. Devuelve los VmIds con
/// <c>AcceptsPreviews=true</c>. No filtra por <c>Status</c> porque una VM transitoriamente
/// desconectada puede recuperarse antes de que el Build termine; si no, el deploy fallará
/// con un error claro y el operador puede ajustar el pool.
/// </summary>
internal sealed class EfPreviewVmPool(VmsDbContext db) : IPreviewVmPool
{
    public async Task<IReadOnlyList<string>> ListAvailableVmIdsAsync(CancellationToken ct)
    {
        var rows = await db.Vms
            .AsNoTracking()
            .Where(v => v.AcceptsPreviews)
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        // El converter de VmId no se aplica al Select directo; hidratamos a string.
        return rows.Select(id => id.ToString()).ToList();
    }
}
