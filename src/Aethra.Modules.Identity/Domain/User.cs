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

    /// <summary>
    /// F12.3 — Username del usuario en GitHub. Lo usa el webhook handler para mapear el
    /// <c>pull_request.user.login</c> a un <see cref="User"/> Aethra y registrar autoría en la
    /// Instance ephemeral. Único globalmente para evitar suplantación: dos users no pueden
    /// reclamar el mismo handle. <c>null</c> hasta que el operador lo configura en su profile.
    /// </summary>
    public string? GitHubUsername { get; private set; }

    // F12.1B — 2FA TOTP (RFC 6238). Secret y recovery codes se persisten cifrados con
    // DataProtection (purpose 'aethra-totp-secrets').
    public byte[]? TotpSecretCipher { get; private set; }
    public bool TotpEnabled { get; private set; }
    public DateTimeOffset? TotpEnabledAt { get; private set; }
    public byte[]? TotpRecoveryCodesCipher { get; private set; }
    public int TotpRecoveryCodesUsedMask { get; private set; }

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

    /// <summary>
    /// F12.3 — Configura el handle de GitHub. <c>null</c>/whitespace limpia el campo. Lanza
    /// <see cref="ArgumentException"/> si el formato no respeta la convención GitHub (alfanumérico
    /// + guion, 1-39 chars, no inicia/termina con guion).
    /// </summary>
    public void SetGitHubUsername(string? gitHubUsername, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(gitHubUsername))
        {
            GitHubUsername = null;
            UpdatedAt = now;
            return;
        }
        var normalized = gitHubUsername.Trim();
        if (normalized.Length > 39)
        {
            throw new ArgumentException("GitHub username no puede exceder 39 caracteres.", nameof(gitHubUsername));
        }
        if (normalized.StartsWith('-') || normalized.EndsWith('-'))
        {
            throw new ArgumentException("GitHub username no puede iniciar o terminar con '-'.", nameof(gitHubUsername));
        }
        foreach (var ch in normalized)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '-'))
            {
                throw new ArgumentException("GitHub username solo admite letras, dígitos y '-'.", nameof(gitHubUsername));
            }
        }
        GitHubUsername = normalized;
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

    // F12.1B — TOTP enrollment lifecycle.

    /// <summary>
    /// Persiste el secret cifrado tras el enroll inicial. El user aun no esta TotpEnabled
    /// hasta que verifique un primer codigo.
    /// </summary>
    public void BeginTotpEnrollment(byte[] secretCipher, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(secretCipher);
        if (secretCipher.Length == 0)
        {
            throw new ArgumentException("TotpSecretCipher no puede estar vacío.", nameof(secretCipher));
        }
        TotpSecretCipher = secretCipher;
        TotpEnabled = false;
        TotpEnabledAt = null;
        TotpRecoveryCodesCipher = null;
        TotpRecoveryCodesUsedMask = 0;
        UpdatedAt = now;
    }

    /// <summary>
    /// Confirma la activacion del 2FA tras verificar el primer codigo y persiste los
    /// recovery codes cifrados.
    /// </summary>
    public void CompleteTotpEnrollment(byte[] recoveryCodesCipher, DateTimeOffset now)
    {
        if (TotpSecretCipher is null || TotpSecretCipher.Length == 0)
        {
            throw new InvalidOperationException("No hay enrollment en curso (TotpSecretCipher vacío).");
        }
        ArgumentNullException.ThrowIfNull(recoveryCodesCipher);
        if (recoveryCodesCipher.Length == 0)
        {
            throw new ArgumentException("RecoveryCodesCipher no puede estar vacío.", nameof(recoveryCodesCipher));
        }
        TotpEnabled = true;
        TotpEnabledAt = now;
        TotpRecoveryCodesCipher = recoveryCodesCipher;
        TotpRecoveryCodesUsedMask = 0;
        UpdatedAt = now;
    }

    /// <summary>Desactiva 2FA y limpia secret + recovery codes.</summary>
    public void DisableTotp(DateTimeOffset now)
    {
        TotpEnabled = false;
        TotpEnabledAt = null;
        TotpSecretCipher = null;
        TotpRecoveryCodesCipher = null;
        TotpRecoveryCodesUsedMask = 0;
        UpdatedAt = now;
    }

    /// <summary>Marca un recovery code como usado por bit index (0..9).</summary>
    public void ConsumeRecoveryCode(int index, DateTimeOffset now)
    {
        var mask = TotpRecoveryCodesUsedMask;
        var bit = 1 << index;
        if ((mask & bit) != 0)
        {
            throw new InvalidOperationException("Recovery code ya fue usado.");
        }
        TotpRecoveryCodesUsedMask = mask | bit;
        UpdatedAt = now;
    }

    /// <summary>
    /// Regenera los recovery codes (manteniendo TOTP activo). Devuelve el cipher nuevo;
    /// se asume que el caller ya cifro la lista nueva.
    /// </summary>
    public void RotateRecoveryCodes(byte[] newCipher, DateTimeOffset now)
    {
        if (!TotpEnabled)
        {
            throw new InvalidOperationException("No se pueden rotar recovery codes sin TOTP activo.");
        }
        ArgumentNullException.ThrowIfNull(newCipher);
        if (newCipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacio.", nameof(newCipher));
        }
        TotpRecoveryCodesCipher = newCipher;
        TotpRecoveryCodesUsedMask = 0;
        UpdatedAt = now;
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
