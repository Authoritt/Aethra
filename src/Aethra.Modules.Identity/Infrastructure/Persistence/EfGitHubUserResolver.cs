using Aethra.Shared.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// F12.3 — Implementación EF de <see cref="IGitHubUserResolver"/>. Query directo sobre
/// <c>users.github_username</c> con índice unique parcial (sólo filas no-null) — lookup O(log n).
/// Match case-insensitive porque GitHub trata handles case-insensitive pero los guardamos
/// como el usuario los tipeó.
/// </summary>
internal sealed class EfGitHubUserResolver(IdentityDbContext db) : IGitHubUserResolver
{
    public async Task<string?> FindByGitHubUsernameAsync(string gitHubUsername, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gitHubUsername))
        {
            return null;
        }
        var normalized = gitHubUsername.Trim();
        // EF Lower comparison fails con valueconverter activo en Id — el campo es string plano, OK.
        var match = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.GitHubUsername != null
                && EF.Functions.ILike(u.GitHubUsername, normalized))
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (match == default)
        {
            return null;
        }
        return match.ToString();
    }
}
