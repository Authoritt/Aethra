using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Domain.Events;
using Aethra.Shared.Contracts.Notes;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Notes.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Note"/>: validación/normalización en Create, la
/// change-detection de <see cref="Note.UpdateBody"/> (solo emite evento/toca UpdatedAt si algo
/// cambió), idempotencia de Pin/Unpin y el manejo de la colección de imágenes (attach/detach).
/// </summary>
public sealed class NoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Note NewNote() => Note.Create(NoteScopeType.Project, "prj_1", "Title", "body", Now);

    [Fact]
    public void Create_normalizes_fields_and_raises_event()
    {
        var note = Note.Create(NoteScopeType.Instance, "  ins_9 ", "  My Note  ", null!, Now, authorId: "  ");

        note.ScopeType.Should().Be(NoteScopeType.Instance);
        note.ScopeId.Should().Be("ins_9");
        note.Title.Should().Be("My Note");
        note.MarkdownBody.Should().BeEmpty();
        note.AuthorId.Should().BeNull();
        note.IsPinned.Should().BeFalse();
        note.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<NoteCreatedEvent>();
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("   ", "Title")]
    [InlineData("prj_1", "")]
    [InlineData("prj_1", "   ")]
    public void Create_throws_on_invalid_scope_id_or_title(string scopeId, string title)
    {
        var act = () => Note.Create(NoteScopeType.Project, scopeId, title, "b", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_on_scope_id_over_64_chars()
    {
        var act = () => Note.Create(NoteScopeType.Project, new string('s', 65), "T", "b", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_on_title_over_255_chars()
    {
        var act = () => Note.Create(NoteScopeType.Project, "prj_1", new string('t', 256), "b", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateBody_changes_title_and_body_and_raises_event()
    {
        var note = NewNote();
        note.ClearDomainEvents();

        note.UpdateBody("New Title", "new body", Now.AddMinutes(1));

        note.Title.Should().Be("New Title");
        note.MarkdownBody.Should().Be("new body");
        note.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<NoteUpdatedEvent>();
    }

    [Fact]
    public void UpdateBody_with_no_actual_change_is_a_noop()
    {
        var note = NewNote();
        var before = note.UpdatedAt;
        note.ClearDomainEvents();

        note.UpdateBody("Title", "body", Now.AddHours(1)); // mismos valores

        note.DomainEvents.Should().BeEmpty();
        note.UpdatedAt.Should().Be(before);
    }

    [Fact]
    public void UpdateBody_null_args_leave_values_unchanged()
    {
        var note = NewNote();
        note.ClearDomainEvents();

        note.UpdateBody(null, null, Now.AddHours(1));

        note.Title.Should().Be("Title");
        note.MarkdownBody.Should().Be("body");
        note.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Pin_and_Unpin_are_idempotent()
    {
        var note = NewNote();

        note.Pin(Now);
        note.IsPinned.Should().BeTrue();
        var afterPin = note.UpdatedAt;
        note.Pin(Now.AddHours(1));
        note.UpdatedAt.Should().Be(afterPin);

        note.Unpin(Now.AddMinutes(1));
        note.IsPinned.Should().BeFalse();
        var afterUnpin = note.UpdatedAt;
        note.Unpin(Now.AddHours(2));
        note.UpdatedAt.Should().Be(afterUnpin);
    }

    [Fact]
    public void AttachImage_adds_to_collection_and_raises_event()
    {
        var note = NewNote();
        note.ClearDomainEvents();
        var imageId = Guid.NewGuid();

        var image = note.AttachImage(imageId, "photo.png", "stored/abc.png", "image/png", 1024, Now);

        note.Images.Should().ContainSingle().Which.Id.Should().Be(imageId);
        image.OriginalFilename.Should().Be("photo.png");
        note.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<NoteImageAttachedEvent>();
    }

    [Fact]
    public void DetachImage_removes_an_existing_image_and_returns_true()
    {
        var note = NewNote();
        var imageId = Guid.NewGuid();
        note.AttachImage(imageId, "p.png", "s.png", "image/png", 10, Now);
        note.ClearDomainEvents();

        note.DetachImage(imageId, Now).Should().BeTrue();

        note.Images.Should().BeEmpty();
        note.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<NoteImageDetachedEvent>();
    }

    [Fact]
    public void DetachImage_returns_false_for_an_unknown_image()
    {
        var note = NewNote();

        note.DetachImage(Guid.NewGuid(), Now).Should().BeFalse();
    }

    [Fact]
    public void MarkDeleted_raises_deleted_event()
    {
        var note = NewNote();
        note.ClearDomainEvents();

        note.MarkDeleted();

        note.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<NoteDeletedEvent>();
    }
}
