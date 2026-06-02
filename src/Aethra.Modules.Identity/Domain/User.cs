using Aethra.Modules.Identity.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Usuario humano autenticado por cookie. Email único, password hasheado con Argon2id
/// y cifrado en BD con DataProtection (encrypt-at-rest sobre el hash — evita que un dump
/// crudo de la tabla exponga hashes susceptibles a cracking offline).
///
/// El soft-delete (<see cref="IsActive"/> = false) permite preservar referencias
/// históricas (notas, deployments) sin perder integridad referencial.
///
/// Los roles se asignan via <see cref="UserRole"/> join entity. El sentido común es
/// que un user puede tener varios roles y el efecto sobre los scopes es la unión.
/// </summary>
public sealed class User : AggregateRoot<UserId>
{
    public string Email { get; private set; }
    public byte[] PasswordHashCipher { get; private set; }
    public string? DisplayName { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<UserRole> _roles = [];
    public IReadOnlyList<UserRole> Roles => _roles.AsReadOnly();

    private User(
        UserId id,
        string email,
        byte[] passwordHashCipher,
        string? displayName,
        DateTimeOffset now) : base(id)
    {
        Email = email;
        PasswordHashCipher = passwordHashCipher;
        DisplayName = displayName;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Crea un nuevo usuario. <paramref name="passwordHashCipher"/> debe venir del codec
    /// (<see cref="IUserPasswordCodec"/>) — el aggregate solo almacena bytes opacos.
    /// </summary>
    public static User Create(
        string email,
        byte[] passwordHashCipher,
        string? displayName,
        DateTimeOffset now)
    {
        ValidateEmail(email);
        ArgumentNullException.ThrowIfNull(passwordHashCipher);
        if (passwordHashCipher.Length == 0)
        {
            throw new ArgumentException("PasswordHashCipher no puede estar vacío.", nameof(passwordHashCipher));
        }
        ValidateDisplayName(displayName);

        var user = new User(UserId.New(), NormalizeEmail(email), passwordHashCipher, displayName?.Trim(), now);
        user.Raise(new UserCreatedEvent(user.Id, user.Email));
        return user;
    }

    public void UpdateDisplayName(string? displayName, DateTimeOffset now)
    {
        ValidateDisplayName(displayName);
        DisplayName = displayName?.Trim();
        UpdatedAt = now;
    }

    public void ResetPassword(byte[] newPasswordHashCipher, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newPasswordHashCipher);
        if (newPasswordHashCipher.Length == 0)
        {
            throw new ArgumentException("PasswordHashCipher no puede estar vacío.", nameof(newPasswordHashCipher));
        }
        PasswordHashCipher = newPasswordHashCipher;
        UpdatedAt = now;
        Raise(new UserPasswordResetEvent(Id));
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }
        IsActive = false;
        UpdatedAt = now;
        Raise(new UserDeactivatedEvent(Id));
    }

    public void Reactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return;
        }
        IsActive = true;
        UpdatedAt = now;
    }

    public void MarkLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
    }

    public void AssignRole(RoleId roleId, DateTimeOffset now)
    {
        if (_roles.Any(r => r.RoleId == roleId))
        {
            return;
        }
        _roles.Add(new UserRole(Id, roleId, now));
        UpdatedAt = now;
    }

    public void UnassignRole(RoleId roleId, DateTimeOffset now)
    {
        var existing = _roles.FirstOrDefault(r => r.RoleId == roleId);
        if (existing is null)
        {
            return;
        }
        _roles.Remove(existing);
        UpdatedAt = now;
    }

    public void ReplaceRoles(IEnumerable<RoleId> roleIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        var distinct = new HashSet<RoleId>(roleIds);
        _roles.RemoveAll(r => !distinct.Contains(r.RoleId));
        foreach (var rid in distinct)
        {
            if (!_roles.Any(r => r.RoleId == rid))
            {
                _roles.Add(new UserRole(Id, rid, now));
            }
        }
        UpdatedAt = now;
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("El email no puede estar vacío.", nameof(email));
        }
        var trimmed = email.Trim();
        if (trimmed.Length > 256)
        {
            throw new ArgumentException("El email no puede exceder 256 caracteres.", nameof(email));
        }
        // Validación mínima: tiene '@' con caracteres a ambos lados. Validar formato
        // RFC completo es ruido — confiamos en que el flujo de verificación (futuro)
        // detecte direcciones inválidas.
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
        {
            throw new ArgumentException("Email inválido.", nameof(email));
        }
    }

    private static void ValidateDisplayName(string? displayName)
    {
        if (displayName is null)
        {
            return;
        }
        var trimmed = displayName.Trim();
        if (trimmed.Length > 100)
        {
            throw new ArgumentException("El display name no puede exceder 100 caracteres.", nameof(displayName));
        }
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // EF Core
    private User() : base()
    {
        Email = string.Empty;
        PasswordHashCipher = [];
    }
}

/// <summary>
/// Join entity para la relación M:N entre <see cref="User"/> y <see cref="Role"/>.
/// PK compuesta (user_id, role_id) — un user no puede tener el mismo rol dos veces.
/// </summary>
public sealed class UserRole
{
    public UserId UserId { get; private set; }
    public RoleId RoleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public UserRole(UserId userId, RoleId roleId, DateTimeOffset assignedAt)
    {
        UserId = userId;
        RoleId = roleId;
        AssignedAt = assignedAt;
    }

    // EF Core
    private UserRole() { }
}
