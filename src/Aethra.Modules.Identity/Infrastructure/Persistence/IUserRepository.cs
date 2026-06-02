using Aethra.Modules.Identity.Domain;

namespace Aethra.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Repositorio del agregado <see cref="User"/>. Los handlers de UI/login usan
/// <see cref="FindByEmailAsync"/> en el path caliente (AsNoTracking); los de management
/// usan <see cref="GetByIdAsync"/> con tracking habilitado.
/// </summary>
public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(UserId id, CancellationToken ct);
    Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
}

/// <summary>
/// Repositorio del agregado <see cref="Role"/>.
/// </summary>
public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct);
    Task<Role?> FindBySlugAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<Role>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Role>> ListByIdsAsync(IEnumerable<RoleId> ids, CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
}
