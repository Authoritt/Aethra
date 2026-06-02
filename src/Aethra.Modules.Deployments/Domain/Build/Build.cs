using Aethra.Modules.Deployments.Domain.Build.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain.Build;

/// <summary>
/// Agregado raíz del pipeline 1 commit → 1 imagen OCI.
///
/// State machine explícita: las transiciones inválidas lanzan <see cref="InvalidOperationException"/>.
/// Los logs son entidades hijas append-only con sequence monotónico (ver <see cref="BuildLogEntry"/>).
///
/// Convención de errores: <see cref="ErrorCode"/> es un slug estable (<c>clone_failed</c>,
/// <c>build_failed</c>, <c>push_failed</c>) consumible por la UI sin parsear el mensaje libre.
/// <see cref="FailedAtStage"/> registra el estado donde se rompió la state machine para que la
/// UI pueda mostrar "Falló en fase Building" sin acoplar la presentación a la enumeración.
/// </summary>
public sealed class Build : AggregateRoot<BuildId>
{
    public string TemplateId { get; private set; }
    public string GitSha { get; private set; }
    public string GitRef { get; private set; }
    public BuildTrigger Trigger { get; private set; }
    public string? TriggeredBy { get; private set; }
    public BuildStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? ImageRef { get; private set; }
    public long? BuildDurationMs { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public BuildStatus? FailedAtStage { get; private set; }

    private long _nextSequence;
    private readonly List<BuildLogEntry> _logs = [];
    public IReadOnlyList<BuildLogEntry> Logs => _logs.AsReadOnly();

    private Build(BuildId id, string templateId, string gitSha, string gitRef, BuildTrigger trigger,
        string? triggeredBy, DateTimeOffset now) : base(id)
    {
        TemplateId = templateId;
        GitSha = gitSha;
        GitRef = gitRef;
        Trigger = trigger;
        TriggeredBy = triggeredBy;
        Status = BuildStatus.Queued;
        CreatedAt = now;
    }

    /// <summary>
    /// Crea un nuevo build en estado <see cref="BuildStatus.Queued"/>. Genera un evento
    /// <see cref="BuildQueuedEvent"/> y una entrada inicial en el log.
    /// </summary>
    public static Build Queue(string templateId, string gitSha, string gitRef, BuildTrigger trigger,
        string? triggeredBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("TemplateId requerido.", nameof(templateId));
        }
        if (string.IsNullOrWhiteSpace(gitSha))
        {
            throw new ArgumentException("GitSha requerido.", nameof(gitSha));
        }
        if (string.IsNullOrWhiteSpace(gitRef))
        {
            throw new ArgumentException("GitRef requerido.", nameof(gitRef));
        }

        var build = new Build(BuildId.New(), templateId, gitSha.Trim().ToLowerInvariant(),
            gitRef.Trim(), trigger, triggeredBy, now);
        build.Raise(new BuildQueuedEvent(build.Id, templateId, build.GitSha));
        var shortSha = build.GitSha.Length >= 7 ? build.GitSha[..7] : build.GitSha;
        build.AppendLog(BuildLogLevel.Info, "queued",
            $"Build encolado: template={templateId}, sha={shortSha}, trigger={trigger}", now);
        return build;
    }

    /// <summary>
    /// Avanza al siguiente estado de la state machine. Lanza si la transición no está permitida.
    /// </summary>
    public void Transition(BuildStatus next, DateTimeOffset now)
    {
        if (!IsValidTransition(Status, next))
        {
            throw new InvalidOperationException(
                $"Transición inválida: {Status} → {next} para build {Id}");
        }
        var from = Status;
        Status = next;
        if (next is BuildStatus.Cloning && StartedAt is null)
        {
            StartedAt = now;
        }
        if (next.IsTerminal())
        {
            FinishedAt = now;
        }
        AppendLog(BuildLogLevel.Info, next.ToString().ToLowerInvariant(), $"→ estado {next}", now);
        Raise(new BuildStatusChangedDomainEvent(Id, from, next));
    }

    /// <summary>
    /// Registra la referencia final de la imagen y la duración total del build (ms). Se llama
    /// justo antes de transicionar a <see cref="BuildStatus.Completed"/>.
    /// </summary>
    public void RecordImageRef(string imageRef, long durationMs, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(imageRef))
        {
            throw new ArgumentException("ImageRef requerido.", nameof(imageRef));
        }
        ImageRef = imageRef.Trim();
        BuildDurationMs = durationMs;
        AppendLog(BuildLogLevel.Info, Status.ToString().ToLowerInvariant(),
            $"Imagen registrada: {ImageRef} ({durationMs} ms)", now);
    }

    /// <summary>
    /// Cierra el build como exitoso. Requiere que <see cref="RecordImageRef"/> se haya
    /// llamado antes (la integración a publicar lleva el image ref).
    /// </summary>
    public void Complete(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ImageRef))
        {
            throw new InvalidOperationException(
                "No se puede completar un build sin ImageRef registrado.");
        }
        Transition(BuildStatus.Completed, now);
        Raise(new BuildCompletedDomainEvent(Id, TemplateId, ImageRef));
    }

    /// <summary>
    /// Marca el build como fallido. Idempotente cuando ya está en estado terminal:
    /// no genera evento adicional para evitar duplicados ante reintentos del orquestador.
    /// <paramref name="durationMs"/> se pasa cuando el orquestador tiene un Stopwatch real
    /// (p.ej. tras un TimeoutException de minutos) — si es null, no se persiste duración,
    /// lo cual es correcto para fallos tempranos (clone fail, template_not_found, etc).
    /// </summary>
    public void Fail(string code, string message, DateTimeOffset now, long? durationMs = null)
    {
        if (Status.IsTerminal())
        {
            return;
        }
        ErrorCode = code;
        ErrorMessage = message;
        FailedAtStage = Status;
        if (durationMs is not null)
        {
            BuildDurationMs = durationMs;
        }
        AppendLog(BuildLogLevel.Error, Status.ToString().ToLowerInvariant(), $"[{code}] {message}", now);
        var from = Status;
        Status = BuildStatus.Failed;
        FinishedAt = now;
        Raise(new BuildStatusChangedDomainEvent(Id, from, BuildStatus.Failed));
        Raise(new BuildFailedDomainEvent(Id, TemplateId, FailedAtStage.Value, code, message));
    }

    /// <summary>
    /// Cancela el build. Solo permitido desde estados tempranos: Queued, Cloning, Building.
    /// Pushing y terminales no son cancelables (la imagen ya pudo persistirse en el registry
    /// y dejarla huérfana es peor que dejar el build registrado como completado).
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status is BuildStatus.Pushing or BuildStatus.Completed
            or BuildStatus.Failed or BuildStatus.Cancelled)
        {
            throw new InvalidOperationException($"No se puede cancelar en estado {Status}.");
        }
        var from = Status;
        Status = BuildStatus.Cancelled;
        FinishedAt = now;
        AppendLog(BuildLogLevel.Warn, "cancelled", "Build cancelado por usuario", now);
        Raise(new BuildStatusChangedDomainEvent(Id, from, BuildStatus.Cancelled));
    }

    /// <summary>
    /// Añade una línea al log. Pensado para uso desde el orquestador (cada step del pipeline
    /// emite líneas) y desde el satélite cuando reenvía stdout/stderr del builder.
    /// </summary>
    public BuildLogEntry AppendLog(BuildLogLevel level, string stage, string text, DateTimeOffset timestamp)
    {
        // `_nextSequence` no se persiste — al cargar el aggregate desde EF, el ctor sin
        // parámetros lo inicializa en 0. Cuando el orchestrator hace AppendLog tras un
        // rehydration, debemos arrancar desde el siguiente sequence disponible sino
        // colisionamos con el log de creación (seq=0). Calcular vez en vez es O(N) pero
        // los logs por build son pocos (decenas).
        var nextSeq = _nextSequence <= 0 && _logs.Count > 0
            ? _logs.Max(l => l.Sequence) + 1
            : _nextSequence;
        var entry = new BuildLogEntry(Id, nextSeq, timestamp, level, stage, text);
        _logs.Add(entry);
        _nextSequence = nextSeq + 1;
        return entry;
    }

    private static bool IsValidTransition(BuildStatus from, BuildStatus to) => (from, to) switch
    {
        (BuildStatus.Queued, BuildStatus.Cloning) => true,
        (BuildStatus.Cloning, BuildStatus.Building) => true,
        (BuildStatus.Building, BuildStatus.Pushing) => true,
        (BuildStatus.Pushing, BuildStatus.Completed) => true,
        // Cancelled solo desde estados tempranos (no desde Pushing ni terminales).
        (BuildStatus.Queued or BuildStatus.Cloning or BuildStatus.Building, BuildStatus.Cancelled) => true,
        _ => false,
    };

    // EF Core
    private Build() : base()
    {
        TemplateId = string.Empty;
        GitSha = string.Empty;
        GitRef = string.Empty;
    }
}
