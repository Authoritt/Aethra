using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Services.Domain;

public enum ScheduledJobRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    TimedOut = 3,
    Cancelled = 4,
}

/// <summary>
/// F12.1A — historial de una ejecucion de <see cref="ScheduledJob"/>. Se trunca
/// stdout/stderr a 64KB para evitar que un job con miles de lineas reviente la tabla.
/// </summary>
public sealed class ScheduledJobRun : AggregateRoot<ScheduledJobRunId>
{
    public const int MaxStreamBytes = 65_536; // 64KB por stream.

    public ScheduledJobId JobId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public ScheduledJobRunStatus Status { get; private set; }
    public int? ExitCode { get; private set; }
    public string? Stdout { get; private set; }
    public string? Stderr { get; private set; }
    public long? DurationMs { get; private set; }

    private ScheduledJobRun(
        ScheduledJobRunId id, ScheduledJobId jobId, DateTimeOffset startedAt) : base(id)
    {
        JobId = jobId;
        StartedAt = startedAt;
        Status = ScheduledJobRunStatus.Running;
    }

    public static ScheduledJobRun Start(ScheduledJobId jobId, DateTimeOffset now)
        => new(ScheduledJobRunId.New(), jobId, now);

    public void MarkCompleted(int exitCode, string? stdout, string? stderr, DateTimeOffset now)
    {
        Status = exitCode == 0 ? ScheduledJobRunStatus.Completed : ScheduledJobRunStatus.Failed;
        ExitCode = exitCode;
        Stdout = TruncateUtf8(stdout, MaxStreamBytes);
        Stderr = TruncateUtf8(stderr, MaxStreamBytes);
        FinishedAt = now;
        DurationMs = (long)(now - StartedAt).TotalMilliseconds;
    }

    public void MarkTimedOut(string? stdout, string? stderr, DateTimeOffset now)
    {
        Status = ScheduledJobRunStatus.TimedOut;
        Stdout = TruncateUtf8(stdout, MaxStreamBytes);
        Stderr = TruncateUtf8(stderr, MaxStreamBytes);
        FinishedAt = now;
        DurationMs = (long)(now - StartedAt).TotalMilliseconds;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = ScheduledJobRunStatus.Failed;
        Stderr = TruncateUtf8(error, MaxStreamBytes);
        FinishedAt = now;
        DurationMs = (long)(now - StartedAt).TotalMilliseconds;
    }

    public void MarkCancelled(DateTimeOffset now)
    {
        Status = ScheduledJobRunStatus.Cancelled;
        FinishedAt = now;
        DurationMs = (long)(now - StartedAt).TotalMilliseconds;
    }

    private static string? TruncateUtf8(string? s, int maxBytes)
    {
        if (string.IsNullOrEmpty(s)) { return null; }
        // Truncado simple por chars; UTF-8 puede sobrepasar pero el limite no es estricto.
        return s.Length <= maxBytes ? s : s[..maxBytes] + "\n[truncated]";
    }

    // EF Core
    private ScheduledJobRun() : base()
    {
    }
}
