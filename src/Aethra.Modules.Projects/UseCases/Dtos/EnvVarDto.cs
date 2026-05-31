namespace Aethra.Modules.Projects.UseCases.Dtos;

public sealed record EnvVarDto(
    string Id,
    string ScopeType,
    string ScopeId,
    string Key,
    string? Value,           // null si IsSecret y el caller no tiene permiso
    bool IsBuildTime,
    bool IsRuntime,
    bool IsSecret,
    bool IsLiteral,
    bool IsMultiline,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record EnvVarResolutionDto(
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> SecretKeys);
