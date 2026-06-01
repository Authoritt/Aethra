using Aethra.Modules.Deployments.Domain.Deployment.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain.Deployment;

/// <summary>
/// Agregado raíz del pipeline de despliegue: <c>1 Build → N Deployments → atomic swap YARP</c>.
///
/// State machine explícita: las transiciones inválidas lanzan <see cref="InvalidOperationException"/>.
/// Los logs son entidades hijas append-only con sequence monotónico (ver <see cref="DeploymentLogEntry"/>).
///
/// <para>
/// El agregado captura referencias al contenedor previo (<see cref="OldContainerId"/>,
/// <see cref="OldImageRef"/>) ANTES de hacer el swap para que un fallo tardío pueda revertir al
/// contenedor anterior. La invariante crítica: solo desde <see cref="DeploymentStatus.Failed"/>
/// se puede transicionar a <see cref="DeploymentStatus.RolledBack"/>; el orquestador llama
/// <see cref="Rollback"/> tras un intento exitoso de restaurar el contenedor viejo.
/// </para>
///
/// <para>
/// <see cref="BuildId"/> y <see cref="InstanceId"/> se persisten como strings — no son FKs físicos
/// porque <c>Build</c> vive en este mismo bounded context pero <c>Instance</c> es cross-module
/// (Projects). Mantener el tipo string evita acoplar el schema a tipos internos de otro módulo
/// y respeta la regla de oro de aislamiento.
/// </para>
/// </summary>
public sealed class Deployment : AggregateRoot<DeploymentId>
{
    public string BuildId { get; private set; }
    public string InstanceId { get; private set; }
    public DeploymentTrigger Trigger { get; private set; }
    public string? TriggeredBy { get; private set; }
    public DeploymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>ID del contenedor previo del Instance (si existe). Capturado antes del swap.</summary>
    public string? OldContainerId { get; private set; }

    /// <summary>ID del contenedor nuevo creado por este deployment.</summary>
    public string? NewContainerId { get; private set; }

    /// <summary>Image ref del contenedor previo (para rollback).</summary>
    public string? OldImageRef { get; private set; }

    /// <summary>Image ref del Build origen — siempre presente al encolar.</summary>
    public string NewImageRef { get; private set; }

    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DeploymentStatus? FailedAtStage { get; private set; }

    private long _nextSequence;
    private readonly List<DeploymentLogEntry> _logs = [];
    public IReadOnlyList<DeploymentLogEntry> Logs => _logs.AsReadOnly();

    private Deployment(DeploymentId id, string buildId, string instanceId, string newImageRef,
        DeploymentTrigger trigger, string? triggeredBy, DateTimeOffset now) : base(id)
    {
        BuildId = buildId;
        InstanceId = instanceId;
        NewImageRef = newImageRef;
        Trigger = trigger;
        TriggeredBy = triggeredBy;
        Status = DeploymentStatus.Pending;
        CreatedAt = now;
    }

    /// <summary>
    /// Crea un nuevo deployment en estado <see cref="DeploymentStatus.Pending"/>. Emite
    /// <see cref="DeploymentQueuedEvent"/> y registra la línea inicial del log.
    /// </summary>
    public static Deployment Queue(string buildId, string instanceId, string newImageRef,
        DeploymentTrigger trigger, string? triggeredBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(buildId))
        {
            throw new ArgumentException("BuildId requerido.", nameof(buildId));
        }
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("InstanceId requerido.", nameof(instanceId));
        }
        if (string.IsNullOrWhiteSpace(newImageRef))
        {
            throw new ArgumentException("NewImageRef requerido.", nameof(newImageRef));
        }

        var deployment = new Deployment(DeploymentId.New(), buildId.Trim(), instanceId.Trim(),
            newImageRef.Trim(), trigger, triggeredBy, now);
        deployment.Raise(new DeploymentQueuedEvent(deployment.Id, deployment.BuildId,
            deployment.InstanceId, deployment.NewImageRef));
        deployment.AppendLog(DeploymentLogLevel.Info, "pending",
            $"Deployment encolado: build={buildId}, instance={instanceId}, trigger={trigger}", now);
        return deployment;
    }

    /// <summary>
    /// Avanza al siguiente estado de la state machine. Lanza si la transición no está permitida.
    /// </summary>
    public void Transition(DeploymentStatus next, DateTimeOffset now)
    {
        if (!IsValidTransition(Status, next))
        {
            throw new InvalidOperationException(
                $"Transición inválida: {Status} → {next} para deployment {Id}");
        }
        var from = Status;
        Status = next;
        if (next is DeploymentStatus.Pulling && StartedAt is null)
        {
            StartedAt = now;
        }
        if (next.IsTerminal())
        {
            FinishedAt = now;
        }
        AppendLog(DeploymentLogLevel.Info, next.ToString().ToLowerInvariant(),
            $"→ estado {next}", now);
        Raise(new DeploymentStatusChangedDomainEvent(Id, from, next));
    }

    /// <summary>
    /// Registra la referencia al contenedor previo del Instance (captura para rollback).
    /// Llamado por el orquestador justo antes de iniciar el swap. <paramref name="containerId"/>
    /// y <paramref name="imageRef"/> pueden venir vacíos en deploys "primera vez" cuando el
    /// Instance aún no tenía contenedor — en ese caso un fallo posterior no podrá hacer rollback.
    /// </summary>
    public void RecordOldContainer(string containerId, string imageRef, DateTimeOffset now)
    {
        OldContainerId = string.IsNullOrWhiteSpace(containerId) ? null : containerId.Trim();
        OldImageRef = string.IsNullOrWhiteSpace(imageRef) ? null : imageRef.Trim();
        var description = OldContainerId is null
            ? "primera vez: no hay contenedor previo en el Instance"
            : $"Contenedor previo capturado: {OldContainerId} ({OldImageRef})";
        AppendLog(DeploymentLogLevel.Info, Status.ToString().ToLowerInvariant(), description, now);
    }

    /// <summary>
    /// Registra la referencia al contenedor nuevo levantado por el satélite. Llamado tras
    /// <c>SendRunAsync</c> exitoso, antes del healthcheck.
    /// </summary>
    public void RecordNewContainer(string containerId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new ArgumentException("ContainerId requerido.", nameof(containerId));
        }
        NewContainerId = containerId.Trim();
        AppendLog(DeploymentLogLevel.Info, Status.ToString().ToLowerInvariant(),
            $"Contenedor nuevo creado: {NewContainerId}", now);
    }

    /// <summary>
    /// Cierra el deployment como exitoso. Requiere que el contenedor nuevo esté registrado y
    /// que el estado actual sea <see cref="DeploymentStatus.Swapping"/>.
    /// </summary>
    public void Complete(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(NewContainerId))
        {
            throw new InvalidOperationException(
                "No se puede completar un deployment sin NewContainerId registrado.");
        }
        Transition(DeploymentStatus.Completed, now);
        Raise(new DeploymentCompletedDomainEvent(Id, InstanceId, NewContainerId, NewImageRef));
    }

    /// <summary>
    /// Marca el deployment como fallido. Idempotente cuando ya está en estado terminal:
    /// no genera evento adicional para evitar duplicados ante reintentos del orquestador.
    /// </summary>
    public void Fail(string code, string message, DateTimeOffset now)
    {
        if (Status.IsTerminal())
        {
            return;
        }
        ErrorCode = code;
        ErrorMessage = message;
        FailedAtStage = Status;
        AppendLog(DeploymentLogLevel.Error, Status.ToString().ToLowerInvariant(),
            $"[{code}] {message}", now);
        var from = Status;
        Status = DeploymentStatus.Failed;
        FinishedAt = now;
        Raise(new DeploymentStatusChangedDomainEvent(Id, from, DeploymentStatus.Failed));
        Raise(new DeploymentFailedDomainEvent(Id, InstanceId, FailedAtStage.Value, code, message));
    }

    /// <summary>
    /// Transición especial <see cref="DeploymentStatus.Failed"/> → <see cref="DeploymentStatus.RolledBack"/>
    /// tras restaurar con éxito el contenedor previo. Solo válida si el estado actual es
    /// <c>Failed</c> y existe <see cref="OldContainerId"/> (no se puede rollbackear sin referencia).
    /// </summary>
    public void Rollback(DateTimeOffset now)
    {
        if (Status != DeploymentStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Solo se puede rollbackear desde Failed; estado actual: {Status}.");
        }
        if (string.IsNullOrWhiteSpace(OldContainerId))
        {
            throw new InvalidOperationException(
                "No se puede rollbackear: no se capturó OldContainerId antes del swap.");
        }
        Status = DeploymentStatus.RolledBack;
        FinishedAt = now;
        AppendLog(DeploymentLogLevel.Warn, "rolledback",
            $"Rollback ejecutado: contenedor previo restaurado ({OldContainerId})", now);
        Raise(new DeploymentStatusChangedDomainEvent(Id, DeploymentStatus.Failed,
            DeploymentStatus.RolledBack));
    }

    /// <summary>
    /// Cancela el deployment. Solo permitido desde estados tempranos (Pending, Pulling): a
    /// partir de Starting el contenedor nuevo ya puede estar arrancando y abortarlo dejaría
    /// recursos huérfanos.
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status is not (DeploymentStatus.Pending or DeploymentStatus.Pulling))
        {
            throw new InvalidOperationException($"No se puede cancelar en estado {Status}.");
        }
        var from = Status;
        Status = DeploymentStatus.Cancelled;
        FinishedAt = now;
        AppendLog(DeploymentLogLevel.Warn, "cancelled", "Deployment cancelado por usuario", now);
        Raise(new DeploymentStatusChangedDomainEvent(Id, from, DeploymentStatus.Cancelled));
    }

    /// <summary>
    /// Añade una línea al log. Pensado para uso desde el orquestador (cada step del pipeline
    /// emite líneas) y desde el satélite cuando reenvía stdout/stderr de los comandos docker.
    /// </summary>
    public DeploymentLogEntry AppendLog(DeploymentLogLevel level, string stage, string text,
        DateTimeOffset timestamp)
    {
        var entry = new DeploymentLogEntry(Id, _nextSequence++, timestamp, level, stage, text);
        _logs.Add(entry);
        return entry;
    }

    private static bool IsValidTransition(DeploymentStatus from, DeploymentStatus to) => (from, to) switch
    {
        (DeploymentStatus.Pending, DeploymentStatus.Pulling) => true,
        (DeploymentStatus.Pulling, DeploymentStatus.Starting) => true,
        (DeploymentStatus.Starting, DeploymentStatus.Healthcheck) => true,
        (DeploymentStatus.Healthcheck, DeploymentStatus.Swapping) => true,
        (DeploymentStatus.Swapping, DeploymentStatus.Completed) => true,
        // Cancelled solo desde estados tempranos (no desde Starting ni después).
        (DeploymentStatus.Pending or DeploymentStatus.Pulling, DeploymentStatus.Cancelled) => true,
        _ => false,
    };

    // EF Core
    private Deployment() : base()
    {
        BuildId = string.Empty;
        InstanceId = string.Empty;
        NewImageRef = string.Empty;
    }
}
