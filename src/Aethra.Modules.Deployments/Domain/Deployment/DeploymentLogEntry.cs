using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain.Deployment;

/// <summary>
/// Nivel de severidad de una línea del log de un deployment.
/// </summary>
public enum DeploymentLogLevel
{
    Info = 0,
    Warn = 1,
    Error = 2,
}

/// <summary>
/// Línea append-only del log de un <see cref="Deployment"/>. Sequence monotónico por deployment
/// para garantizar orden estable aunque las líneas lleguen fuera de tiempo desde el satélite.
///
/// La etapa (<see cref="Stage"/>) acompaña cada línea con uno de los estados del pipeline
/// — <c>pulling</c>, <c>starting</c>, <c>healthcheck</c>, <c>swapping</c> — para que el frontend
/// pueda agrupar los logs por fase sin tener que correlacionar contra el status del deployment.
/// </summary>
public sealed class DeploymentLogEntry : Entity<DeploymentLogId>
{
    public DeploymentId DeploymentId { get; private set; }
    public long Sequence { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public DeploymentLogLevel Level { get; private set; }
    public string Stage { get; private set; }
    public string Text { get; private set; }

    internal DeploymentLogEntry(DeploymentId deploymentId, long sequence, DateTimeOffset timestamp,
        DeploymentLogLevel level, string stage, string text) : base(DeploymentLogId.New())
    {
        DeploymentId = deploymentId;
        Sequence = sequence;
        Timestamp = timestamp;
        Level = level;
        Stage = stage;
        Text = text;
    }

    // EF Core
    private DeploymentLogEntry() : base()
    {
        Stage = string.Empty;
        Text = string.Empty;
    }
}
