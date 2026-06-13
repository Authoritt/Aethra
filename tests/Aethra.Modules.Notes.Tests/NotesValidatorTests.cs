using Aethra.Modules.Notes.Application.Commands;
using Aethra.Shared.Contracts.Notes;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Notes.Tests;

/// <summary>
/// Tests de los validators FluentValidation de Notes (CreateNote + UpsertPinnedFact): required +
/// límites de longitud y NotNull del body/value (que puede ser cadena vacía pero no null).
/// </summary>
public sealed class NotesValidatorTests
{
    // ---------- CreateNote ----------

    private static CreateNoteCommand NewNote(string scopeId = "prj_1", string title = "Title", string body = "body")
        => new(NoteScopeType.Project, scopeId, title, body, null);

    [Fact]
    public void CreateNote_accepts_a_valid_command()
        => new CreateNoteValidator().Validate(NewNote()).IsValid.Should().BeTrue();

    [Fact]
    public void CreateNote_requires_scope_id_and_title()
    {
        new CreateNoteValidator().Validate(NewNote(scopeId: "")).IsValid.Should().BeFalse();
        new CreateNoteValidator().Validate(NewNote(title: "")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateNote_rejects_null_markdown_body_but_allows_empty()
    {
        new CreateNoteValidator().Validate(NewNote(body: null!)).IsValid.Should().BeFalse();
        new CreateNoteValidator().Validate(NewNote(body: "")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateNote_rejects_scope_id_over_64_and_title_over_255()
    {
        new CreateNoteValidator().Validate(NewNote(scopeId: new string('s', 65))).IsValid.Should().BeFalse();
        new CreateNoteValidator().Validate(NewNote(title: new string('t', 256))).IsValid.Should().BeFalse();
    }

    // ---------- UpsertPinnedFact ----------

    private static UpsertPinnedFactCommand NewFact(
        string scopeId = "ins_1", string key = "admin_password", string value = "secret", string? description = null)
        => new(NoteScopeType.Instance, scopeId, key, value, true, description);

    [Fact]
    public void UpsertPinnedFact_accepts_a_valid_command()
        => new UpsertPinnedFactValidator().Validate(NewFact()).IsValid.Should().BeTrue();

    [Fact]
    public void UpsertPinnedFact_requires_scope_id_key_and_non_null_value()
    {
        new UpsertPinnedFactValidator().Validate(NewFact(scopeId: "")).IsValid.Should().BeFalse();
        new UpsertPinnedFactValidator().Validate(NewFact(key: "")).IsValid.Should().BeFalse();
        new UpsertPinnedFactValidator().Validate(NewFact(value: null!)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpsertPinnedFact_rejects_key_over_128_chars()
        => new UpsertPinnedFactValidator().Validate(NewFact(key: new string('k', 129))).IsValid.Should().BeFalse();

    [Fact]
    public void UpsertPinnedFact_rejects_description_over_500_chars()
        => new UpsertPinnedFactValidator().Validate(NewFact(description: new string('d', 501))).IsValid.Should().BeFalse();
}
