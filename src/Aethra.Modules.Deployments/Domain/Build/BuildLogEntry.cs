using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain.Build;

/// <summary>
/// Nivel de severidad de una línea del log de un build.
/// </summary>
public enum BuildLogLevel
{
    Info = 0,
    Warn = 1,
    Error = 2,
}

/// <summary>
/// Línea append-only del log de un <see cref="Build"/>. Sequence monotónico por build para
/// garantizar orden estable aunque las líneas lleguen fuera de tiempo desde el satélite.
///
/// La etapa (<see cref="Stage"/>) acompaña cada línea con uno de los estados del pipeline
/// — <c>cloning</c>, <c>building</c>, <c>pushing</c> — para que el frontend pueda agrupar
/// los logs por fase sin tener que correlacionar contra el status del build.
/// </summary>
public sealed class BuildLogEntry : Entity<BuildLogId>
{
    public BuildId BuildId { get; private set; }
    public long Sequence { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public BuildLogLevel Level { get; private set; }
    public string Stage { get; private set; }
    public string Text { get; private set; }

    internal BuildLogEntry(BuildId buildId, long sequence, DateTimeOffset timestamp,
        BuildLogLevel level, string stage, string text) : base(BuildLogId.New())
    {
        BuildId = buildId;
        Sequence = sequence;
        Timestamp = timestamp;
        Level = level;
        Stage = stage;
        Text = text;
    }

    // EF Core
    private BuildLogEntry() : base()
    {
        Stage = string.Empty;
        Text = string.Empty;
    }
}
