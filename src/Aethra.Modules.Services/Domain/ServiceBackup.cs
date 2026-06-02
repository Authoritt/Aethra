using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Services.Domain;

public enum ServiceBackupStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
}

/// <summary>
/// Registro de una operacion de backup individual. Persiste metadata (status, tamaño, ruta de
/// destino, error si falla) — el blob binario vive en <see cref="DestinationPath"/>
/// (volume://... o s3://...).
/// </summary>
public sealed class ServiceBackup : AggregateRoot<ServiceBackupId>
{
    public ManagedServiceId ServiceId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public ServiceBackupStatus Status { get; private set; }
    public long? SizeBytes { get; private set; }
    public string DestinationPath { get; private set; }
    public string? ErrorMessage { get; private set; }

    private ServiceBackup(
        ServiceBackupId id,
        ManagedServiceId serviceId,
        string destinationPath,
        DateTimeOffset now) : base(id)
    {
        ServiceId = serviceId;
        DestinationPath = destinationPath;
        Status = ServiceBackupStatus.Running;
        StartedAt = now;
    }

    public static ServiceBackup Start(ManagedServiceId serviceId, string destinationPath, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("DestinationPath requerido.", nameof(destinationPath));
        }
        return new ServiceBackup(ServiceBackupId.New(), serviceId, destinationPath, now);
    }

    public void MarkCompleted(long sizeBytes, DateTimeOffset now)
    {
        Status = ServiceBackupStatus.Completed;
        SizeBytes = sizeBytes;
        FinishedAt = now;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset now)
    {
        Status = ServiceBackupStatus.Failed;
        ErrorMessage = Truncate(errorMessage, 2000);
        FinishedAt = now;
    }

    /// <summary>
    /// Tras escribir al storage el path final puede incluir el filename real (volume:// o s3://).
    /// El orquestador llama este metodo justo antes de <see cref="MarkCompleted"/>.
    /// </summary>
    public void UpdateDestinationPath(string finalPath)
    {
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            throw new ArgumentException("FinalPath requerido.", nameof(finalPath));
        }
        DestinationPath = finalPath;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max]);

    // EF Core
    private ServiceBackup() : base()
    {
        DestinationPath = string.Empty;
    }
}
