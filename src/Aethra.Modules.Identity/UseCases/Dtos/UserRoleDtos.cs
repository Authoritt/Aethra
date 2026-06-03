namespace Aethra.Modules.Identity.UseCases.Dtos;

public sealed record UserSummaryDto(
    string Id,
    string Email,
    string? DisplayName,
    IReadOnlyList<RoleRefDto> Roles,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? GitHubUsername = null);

public sealed record RoleRefDto(
    string Id,
    string Slug,
    string DisplayName);

public sealed record RoleDto(
    string Id,
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Scopes,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatedUserDto(
    string Id,
    string Email,
    string? DisplayName,
    IReadOnlyList<RoleRefDto> Roles);

public sealed record CreatedRoleDto(
    string Id,
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Scopes);

public sealed record ResetPasswordResultDto(string Id, string Email);
