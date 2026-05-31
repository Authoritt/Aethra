using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.EnvVars;

/// <summary>
/// Variable de entorno con scope polimórfico. La misma tabla almacena variables a nivel
/// Project, Environment y Application — se distingue por (<see cref="ScopeType"/>, <see cref="ScopeId"/>).
///
/// Resolución (lo más cercano gana): Application &gt; Environment &gt; Project.
/// Ver <see cref="EnvVarResolver"/>.
/// </summary>
public sealed class EnvironmentVariable : Entity<EnvVarId>
{
    public EnvScopeType ScopeType { get; private set; }
    public string ScopeId { get; private set; }    // string para soportar IDs heterogéneos (prj_*, env_*, app_*)
    public string Key { get; private set; }
    public string Value { get; private set; }
    public bool IsBuildTime { get; private set; }
    public bool IsRuntime { get; private set; }
    public bool IsSecret { get; private set; }
    public bool IsLiteral { get; private set; }    // si true, no interpolar ${...} en el valor
    public bool IsMultiline { get; private set; }
    /// <summary>
    /// Origen lógico de la variable. <c>null</c> = creada manualmente por usuario.
    /// Valores conocidos: <c>"binding:bnd_..."</c> (inyectada por un ServiceBinding F5).
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
        bool isSecret,
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
        IsSecret = isSecret;
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
        bool isSecret = false,
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
            isSecret,
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

    public void UpdateFlags(bool? isBuildTime, bool? isRuntime, bool? isSecret, bool? isLiteral, bool? isMultiline,
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
        if (isSecret is not null)
        {
            IsSecret = isSecret.Value;
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
