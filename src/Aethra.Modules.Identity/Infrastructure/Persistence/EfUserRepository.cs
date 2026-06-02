using Aethra.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.Infrastructure.Persistence;

internal sealed class EfUserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<User?>(null);
        }
        var normalized = User.NormalizeEmail(email);
        return db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Email == normalized, ct);
    }

    public Task<User?> GetByIdAsync(UserId id, CancellationToken ct)
        => db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct)
    {
        var items = await db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return items;
    }

    public Task<int> CountAsync(CancellationToken ct) => db.Users.CountAsync(ct);
}

internal sealed class EfRoleRepository(IdentityDbContext db) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct)
        => db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Role?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Task.FromResult<Role?>(null);
        }
        var normalized = slug.Trim().ToLowerInvariant();
        return db.Roles.FirstOrDefaultAsync(r => r.Slug == normalized, ct);
    }

    public async Task<IReadOnlyList<Role>> ListAllAsync(CancellationToken ct)
    {
        var items = await db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Slug)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return items;
    }

    public async Task<IReadOnlyList<Role>> ListByIdsAsync(IEnumerable<RoleId> ids, CancellationToken ct)
    {
        var list = ids?.Distinct().ToList() ?? [];
        if (list.Count == 0)
        {
            return [];
        }
        var items = await db.Roles
            .AsNoTracking()
            .Where(r => list.Contains(r.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return items;
    }

    public Task<int> CountAsync(CancellationToken ct) => db.Roles.CountAsync(ct);
}
