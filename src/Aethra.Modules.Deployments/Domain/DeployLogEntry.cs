using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain;

public enum DeployLogLevel
{
    Info = 0,
    Warn = 1,
    Error = 2,
}

/// <summary>
/// Línea append-only del log de un <see cref="DeployJob"/>.
/// Sequence monotónico por job para garantizar orden incluso si llegan fuera de tiempo.
/// </summary>
public sealed class DeployLogEntry : Entity<DeployLogId>
{
    public DeployJobId JobId { get; private set; }
    public long Sequence { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public DeployLogLevel Level { get; private set; }
    public string Stage { get; private set; }
    public string Text { get; private set; }

    internal DeployLogEntry(DeployJobId jobId, long sequence, DateTimeOffset timestamp, DeployLogLevel level,
        string stage, string text) : base(DeployLogId.New())
    {
        JobId = jobId;
        Sequence = sequence;
        Timestamp = timestamp;
        Level = level;
        Stage = stage;
        Text = text;
    }

    // EF Core
    private DeployLogEntry() : base() { Stage = string.Empty; Text = string.Empty; }
}
