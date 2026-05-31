using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.EnvVars;

/// <summary>
/// Variable de entorno (no secreta) con scope polimórfico. La misma tabla almacena variables a
/// nivel Project, Template, Client e Instance — se distingue por (<see cref="ScopeType"/>,
/// <see cref="ScopeId"/>).
///
/// Resolución (lo más cercano gana): Instance &gt; Client &gt; Template &gt; Project.
/// Los secretos viven en una tabla separada (ver <c>ISecretWriter</c>) — esta clase NO porta
/// el flag <c>IsSecret</c>: F9.0 simplifica el modelo dividiendo secretos en otra entidad.
/// </summary>
public sealed class EnvironmentVariable : Entity<EnvVarId>
{
    public EnvScopeType ScopeType { get; private set; }
    /// <summary>
    /// ID textual del scope. Heterogéneo: <c>prj_*</c>, <c>tpl_*</c>, <c>cli_*</c> o <c>ins_*</c>
    /// según <see cref="ScopeType"/>. Mantener string evita FK polimórfico en BD.
    /// </summary>
    public string ScopeId { get; private set; }
    public string Key { get; private set; }
    public string Value { get; private set; }
    public bool IsBuildTime { get; private set; }
    public bool IsRuntime { get; private set; }
    /// <summary>Si <c>true</c>, no interpolar <c>${...}</c> en el valor.</summary>
    public bool IsLiteral { get; private set; }
    public bool IsMultiline { get; private set; }
    /// <summary>
    /// Origen lógico de la variable. <c>null</c> = creada manualmente por usuario.
    /// Valores conocidos: <c>"binding:bnd_..."</c> (inyectada por un ServiceBinding).
    /// Permite revoke selectivo sin pisar overrides manuales.
    /// </summary>
    public string? Source { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private EnvironmentVariable(
        EnvVarId id,
        EnvScopeType scopeType,
        string scopeId,
        string key,
        string value,
        bool isBuildTime,
        bool isRuntime,
        bool isLiteral,
        bool isMultiline,
        string? source,
        DateTimeOffset now) : base(id)
    {
        ScopeType = scopeType;
        ScopeId = scopeId;
        Key = key;
        Value = value;
        IsBuildTime = isBuildTime;
        IsRuntime = isRuntime;
        IsLiteral = isLiteral;
        IsMultiline = isMultiline;
        Source = source;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static EnvironmentVariable Create(
        EnvScopeType scopeType,
        string scopeId,
        string key,
        string value,
        DateTimeOffset now,
        bool isBuildTime = true,
        bool isRuntime = true,
        bool isLiteral = false,
        bool isMultiline = false,
        string? source = null)
    {
        ValidateKey(key);
        return new EnvironmentVariable(
            EnvVarId.New(),
            scopeType,
            scopeId,
            key.Trim(),
            value ?? string.Empty,
            isBuildTime,
            isRuntime,
            isLiteral,
            isMultiline,
            source,
            now);
    }

    public void UpdateValue(string value, DateTimeOffset now)
    {
        Value = value ?? string.Empty;
        UpdatedAt = now;
    }

    public void UpdateFlags(bool? isBuildTime, bool? isRuntime, bool? isLiteral, bool? isMultiline,
        DateTimeOffset now)
    {
        if (isBuildTime is not null)
        {
            IsBuildTime = isBuildTime.Value;
        }
        if (isRuntime is not null)
        {
            IsRuntime = isRuntime.Value;
        }
        if (isLiteral is not null)
        {
            IsLiteral = isLiteral.Value;
        }
        if (isMultiline is not null)
        {
            IsMultiline = isMultiline.Value;
        }
        UpdatedAt = now;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("La clave de la variable no puede estar vacía.", nameof(key));
        }
        var k = key.Trim();
        if (k.Length > 256)
        {
            throw new ArgumentException("La clave no puede exceder 256 caracteres.", nameof(key));
        }
        foreach (var c in k)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                throw new ArgumentException(
                    "La clave solo admite letras, dígitos y guion bajo (convención POSIX).", nameof(key));
            }
        }
    }

    // EF Core
    private EnvironmentVariable() : base() { ScopeId = string.Empty; Key = string.Empty; Value = string.Empty; }
}
