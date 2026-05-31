namespace Aethra.Modules.Notes.Application.Dtos;

/// <summary>
/// DTOs de lectura del módulo Notes. Convención: PascalCase en C# → camelCase en JSON
/// (default de minimal APIs de .NET 10 web defaults).
/// </summary>
public sealed record NoteSummaryDto(
    string Id,
    string ScopeType,
    string ScopeId,
    string Title,
    bool IsPinned,
    int ImageCount,
    string? AuthorId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record NoteDetailDto(
    string Id,
    string ScopeType,
    string ScopeId,
    string Title,
    string MarkdownBody,
    bool IsPinned,
    string? AuthorId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NoteImageDto> Images);

public sealed record NoteImageDto(
    Guid ImageId,
    string OriginalFilename,
    string ContentType,
    int SizeBytes,
    DateTimeOffset UploadedAt,
    string Url);

public sealed record PinnedFactDto(
    string Id,
    string ScopeType,
    string ScopeId,
    string Key,
    string Value,           // <c>"********"</c> si IsSecret=true y reveal=false; el texto plano en cualquier otro caso.
    bool IsSecret,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
