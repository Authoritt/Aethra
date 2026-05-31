using Aethra.Modules.Notes.Application.Dtos;
using Aethra.Modules.Notes.Domain;

namespace Aethra.Modules.Notes.Application;

internal static class NoteMapper
{
    public static NoteSummaryDto ToSummary(Note note) => new(
        Id: note.Id.ToString(),
        ScopeType: note.ScopeType.ToString(),
        ScopeId: note.ScopeId,
        Title: note.Title,
        IsPinned: note.IsPinned,
        ImageCount: note.Images.Count,
        AuthorId: note.AuthorId,
        CreatedAt: note.CreatedAt,
        UpdatedAt: note.UpdatedAt);

    public static NoteDetailDto ToDetail(Note note) => new(
        Id: note.Id.ToString(),
        ScopeType: note.ScopeType.ToString(),
        ScopeId: note.ScopeId,
        Title: note.Title,
        MarkdownBody: note.MarkdownBody,
        IsPinned: note.IsPinned,
        AuthorId: note.AuthorId,
        CreatedAt: note.CreatedAt,
        UpdatedAt: note.UpdatedAt,
        Images: [.. note.Images.Select(ToImageDto)]);

    public static NoteImageDto ToImageDto(NoteImage image) => new(
        ImageId: image.Id,
        OriginalFilename: image.OriginalFilename,
        ContentType: image.ContentType,
        SizeBytes: image.SizeBytes,
        UploadedAt: image.UploadedAt,
        Url: $"/api/notes/images/{image.Id:N}");
}
