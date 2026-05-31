using Aethra.Modules.Notes.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Notes.Infrastructure.Conversions;

/// <summary>
/// Conversores EF Core para value-object IDs → string en BD. Mismo patrón que
/// <c>Aethra.Modules.Projects.Infrastructure.Conversions.ValueConverters</c>: helpers
/// estáticos para evitar <c>out var</c> dentro de expression-trees compilados por EF.
/// </summary>
public static class ValueConverters
{
    public static readonly ValueConverter<NoteId, string> NoteIdConverter = new(
        id => id.ToString(),
        s => ParseNoteId(s));

    public static readonly ValueConverter<PinnedFactId, string> PinnedFactIdConverter = new(
        id => id.ToString(),
        s => ParsePinnedFactId(s));

    private static NoteId ParseNoteId(string s)
        => AethraId.TryParse(s, out var parsed) ? new NoteId(parsed.Value) : default;

    private static PinnedFactId ParsePinnedFactId(string s)
        => AethraId.TryParse(s, out var parsed) ? new PinnedFactId(parsed.Value) : default;
}
