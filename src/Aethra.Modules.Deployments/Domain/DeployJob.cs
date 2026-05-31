using Aethra.Modules.Deployments.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain;

/// <summary>
/// Agregado raíz del proceso de deploy. State machine explícita: las transiciones
/// inválidas lanzan <see cref="InvalidOperationException"/>.
/// Los logs son entidades hijas append-only con sequence monotónico.
/// </summary>
public sealed class DeployJob : AggregateRoot<DeployJobId>
{
    public string ApplicationId { get; private set; }
    public string GitSha { get; private set; }
    public string Branch { get; private set; }
    public DeployTrigger Trigger { get; private set; }
    public string? TriggeredBy { get; private set; }
    public DeployStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public string? ImageTag { get; private set; }
    public string? ContainerName { get; private set; }
    public int? ContainerPort { get; private set; }

    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DeployStatus? FailedAtStage { get; private set; }

    private long _nextSequence;
    private readonly List<DeployLogEntry> _logs = [];
    public IReadOnlyList<DeployLogEntry> Logs => _logs.AsReadOnly();

    private DeployJob(DeployJobId id, string applicationId, string gitSha, string branch, DeployTrigger trigger,
        string? triggeredBy, DateTimeOffset now) : base(id)
    {
        ApplicationId = applicationId;
        GitSha = gitSha;
        Branch = branch;
        Trigger = trigger;
        TriggeredBy = triggeredBy;
        Status = DeployStatus.Queued;
        CreatedAt = now;
    }

    public static DeployJob Queue(string applicationId, string gitSha, string branch, DeployTrigger trigger,
        string? triggeredBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("ApplicationId requerido.", nameof(applicationId));
        }
        if (string.IsNullOrWhiteSpace(gitSha))
        {
            throw new ArgumentException("GitSha requerido.", nameof(gitSha));
        }
        var job = new DeployJob(DeployJobId.New(), applicationId, gitSha.Trim().ToLowerInvariant(),
            branch.Trim(), trigger, triggeredBy, now);
        job.Raise(new DeployJobQueuedEvent(job.Id, applicationId, job.GitSha));
        job.AppendLog(DeployLogLevel.Info, "queued",
            $"Deploy encolado: app={applicationId}, sha={job.GitSha[..7]}, trigger={trigger}", now);
        return job;
    }

    public void Transition(DeployStatus next, DateTimeOffset now)
    {
        var allowed = IsValidTransition(Status, next);
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Transición inválida: {Status} → {next} para job {Id}");
        }
        var from = Status;
        Status = next;
        if (next is DeployStatus.Cloning && StartedAt is null)
        {
            StartedAt = now;
        }
        if (next.IsTerminal())
        {
            FinishedAt = now;
        }
        AppendLog(DeployLogLevel.Info, next.ToString().ToLowerInvariant(), $"→ estado {next}", now);
        Raise(new DeployStatusChangedDomainEvent(Id, from, next));
    }

    public void RecordBuildResult(string imageTag, DateTimeOffset now)
    {
        ImageTag = imageTag;
        AppendLog(DeployLogLevel.Info, "building", $"Build OK: image={imageTag}", now);
    }

    public void RecordRunResult(string containerName, int containerPort, DateTimeOffset now)
    {
        ContainerName = containerName;
        ContainerPort = containerPort;
        AppendLog(DeployLogLevel.Info, "swapping", $"Container corriendo: {containerName}:{containerPort}", now);
    }

    public void Complete(DateTimeOffset now)
    {
        Transition(DeployStatus.Completed, now);
        Raise(new DeployJobCompletedDomainEvent(Id, ApplicationId, ContainerName ?? string.Empty,
            ContainerPort ?? 0));
    }

    public void Fail(string code, string message, DateTimeOffset now)
    {
        if (Status.IsTerminal())
        {
            return;
        }
        ErrorCode = code;
        ErrorMessage = message;
        FailedAtStage = Status;
        AppendLog(DeployLogLevel.Error, Status.ToString().ToLowerInvariant(), $"[{code}] {message}", now);
        var from = Status;
        Status = DeployStatus.Failed;
        FinishedAt = now;
        Raise(new DeployStatusChangedDomainEvent(Id, from, DeployStatus.Failed));
        Raise(new DeployJobFailedDomainEvent(Id, ApplicationId, FailedAtStage.Value, code, message));
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is DeployStatus.Swapping or DeployStatus.Completed or DeployStatus.Failed or DeployStatus.Cancelled)
        {
            throw new InvalidOperationException($"No se puede cancelar en estado {Status}.");
        }
        var from = Status;
        Status = DeployStatus.Cancelled;
        FinishedAt = now;
        AppendLog(DeployLogLevel.Warn, "cancelled", "Deploy cancelado por usuario", now);
        Raise(new DeployStatusChangedDomainEvent(Id, from, DeployStatus.Cancelled));
    }

    public DeployLogEntry AppendLog(DeployLogLevel level, string stage, string text, DateTimeOffset timestamp)
    {
        var entry = new DeployLogEntry(Id, _nextSequence++, timestamp, level, stage, text);
        _logs.Add(entry);
        return entry;
    }

    private static bool IsValidTransition(DeployStatus from, DeployStatus to) => (from, to) switch
    {
        (DeployStatus.Queued, DeployStatus.Cloning) => true,
        (DeployStatus.Cloning, DeployStatus.Building) => true,
        (DeployStatus.Building, DeployStatus.Healthcheck) => true,
        (DeployStatus.Healthcheck, DeployStatus.Swapping) => true,
        (DeployStatus.Swapping, DeployStatus.Completed) => true,
        // Cancelled puede llegar desde estados tempranos (no desde swapping ni terminales).
        (DeployStatus.Queued or DeployStatus.Cloning or DeployStatus.Building, DeployStatus.Cancelled) => true,
        _ => false,
    };

    // EF Core
    private DeployJob() : base()
    {
        ApplicationId = string.Empty;
        GitSha = string.Empty;
        Branch = string.Empty;
    }
}
