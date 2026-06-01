namespace Aethra.Modules.Settings.UseCases.Dtos;

public sealed record EnvironmentDefinitionDto(
    string Id,
    string Slug,
    string DisplayName,
    int Order,
    DateTimeOffset CreatedAt);
