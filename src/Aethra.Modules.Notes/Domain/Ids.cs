using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Identificador de una <see cref="Note"/>. Prefijo estable <c>note_</c>.
/// </summary>
public readonly record struct NoteId(AethraId Value)
{
    public static NoteId New() => new(AethraId.NewId("note"));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identificador de un <see cref="PinnedFact"/>. Prefijo estable <c>pf_</c>.
/// </summary>
public readonly record struct PinnedFactId(AethraId Value)
{
    public static PinnedFactId New() => new(AethraId.NewId("pf"));
    public override string ToString() => Value.ToString();
}
