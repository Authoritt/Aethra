using Aethra.Modules.Notes.Domain.Events;
using Aethra.Shared.Contracts.Notes;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Nota markdown con scope polimórfico (Project/Template/Client/Instance). El cuerpo es
/// markdown plano; el render se hace en el cliente. Puede tener imágenes adjuntas (owned
/// entities <see cref="NoteImage"/>) cuyo binario vive fuera de BD via <see cref="INoteImageStore"/>.
///
/// El flag <see cref="IsPinned"/> destaca la nota en la vista del scope (orden + badge).
/// </summary>
public sealed class Note : AggregateRoot<NoteId>
{
    public NoteScopeType ScopeType { get; private set; }
    public string ScopeId { get; private set; }
    public string Title { get; private set; }
    public string MarkdownBody { get; private set; }
    public bool IsPinned { get; private set; }
    public string? AuthorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<NoteImage> _images = [];
    public IReadOnlyList<NoteImage> Images => _images.AsReadOnly();

    private Note(
        NoteId id,
        NoteScopeType scopeType,
        string scopeId,
        string title,
        string markdownBody,
        string? authorId,
        DateTimeOffset now) : base(id)
    {
        ScopeType = scopeType;
        ScopeId = scopeId;
        Title = title;
        MarkdownBody = markdownBody;
        AuthorId = authorId;
        IsPinned = false;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Note Create(
        NoteScopeType scopeType,
        string scopeId,
        string title,
        string markdownBody,
        DateTimeOffset now,
        string? authorId = null)
    {
        ValidateScopeId(scopeId);
        ValidateTitle(title);
        var note = new Note(
            NoteId.New(),
            scopeType,
            scopeId.Trim(),
            title.Trim(),
            markdownBody ?? string.Empty,
            string.IsNullOrWhiteSpace(authorId) ? null : authorId.Trim(),
            now);
        note.Raise(new NoteCreatedEvent(note.Id, scopeType, note.ScopeId, note.Title));
        return note;
    }

    public void UpdateBody(string? title, string? markdownBody, DateTimeOffset now)
    {
        var changed = false;
        if (title is not null)
        {
            ValidateTitle(title);
            var trimmed = title.Trim();
            if (trimmed != Title)
            {
                Title = trimmed;
                changed = true;
            }
        }
        if (markdownBody is not null && markdownBody != MarkdownBody)
        {
            MarkdownBody = markdownBody;
            changed = true;
        }
        if (changed)
        {
            UpdatedAt = now;
            Raise(new NoteUpdatedEvent(Id, Title));
        }
    }

    public void Pin(DateTimeOffset now)
    {
        if (IsPinned)
        {
            return;
        }
        IsPinned = true;
        UpdatedAt = now;
    }

    public void Unpin(DateTimeOffset now)
    {
        if (!IsPinned)
        {
            return;
        }
        IsPinned = false;
        UpdatedAt = now;
    }

    public NoteImage AttachImage(
        Guid imageId,
        string originalFilename,
        string storedFilename,
        string contentType,
        int sizeBytes,
        DateTimeOffset now)
    {
        var image = NoteImage.Create(imageId, originalFilename, storedFilename, contentType, sizeBytes, now);
        _images.Add(image);
        UpdatedAt = now;
        Raise(new NoteImageAttachedEvent(Id, image.Id, image.OriginalFilename));
        return image;
    }

    public bool DetachImage(Guid imageId, DateTimeOffset now)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is null)
        {
            return false;
        }
        _images.Remove(image);
        UpdatedAt = now;
        Raise(new NoteImageDetachedEvent(Id, imageId));
        return true;
    }

    public void MarkDeleted()
    {
        Raise(new NoteDeletedEvent(Id, ScopeType, ScopeId));
    }

    private static void ValidateScopeId(string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentException("El scopeId no puede estar vacío.", nameof(scopeId));
        }
        if (scopeId.Length > 64)
        {
            throw new ArgumentException("El scopeId no puede exceder 64 caracteres.", nameof(scopeId));
        }
    }

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("El título no puede estar vacío.", nameof(title));
        }
        if (title.Trim().Length > 255)
        {
            throw new ArgumentException("El título no puede exceder 255 caracteres.", nameof(title));
        }
    }

    // EF Core
    private Note() : base()
    {
        ScopeId = string.Empty;
        Title = string.Empty;
        MarkdownBody = string.Empty;
    }
}
