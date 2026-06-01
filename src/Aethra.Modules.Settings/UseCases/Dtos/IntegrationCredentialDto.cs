using Aethra.Modules.Settings.Domain;

namespace Aethra.Modules.Settings.UseCases.Dtos;

/// <summary>
/// Resumen visible en la UI/list. NO incluye el valor descifrado — solo metadata.
/// </summary>
public sealed record IntegrationCredentialDto(
    string Id,
    string Name,
    IntegrationCredentialType Type,
    string DisplayName,
    string? Description,
    IReadOnlyDictionary<string, string>? Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? LastUsedAt);
