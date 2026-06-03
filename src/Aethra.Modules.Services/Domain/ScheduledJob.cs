using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Services.Domain;

/// <summary>
/// F12.1A — un job programado que se ejecuta dentro de un <see cref="ManagedService"/>
/// container vía <c>docker exec</c>. Modelo "Dokploy parity": un servicio puede tener N
/// jobs (ej. <c>0 2 * * * pg_dump -d myapp > /backup/dump.sql</c>) que el worker dispara
/// segun cron + zona horaria.
///
/// Cada ejecucion genera un <see cref="ScheduledJobRun"/> con stdout/stderr capturados.
/// </summary>
public sealed class ScheduledJob : AggregateRoot<ScheduledJobId>
{
    public ManagedServiceId ServiceId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string Command { get; private set; }
    public string CronExpression { get; private set; }
    public string TimeZone { get; private set; }
    public bool Enabled { get; private set; }
    public int MaxConcurrent { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
    public DateTimeOffset? NextRunAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScheduledJob(
        ScheduledJobId id,
        ManagedServiceId serviceId,
        string name,
        string? description,
        string command,
        string cronExpression,
        string timeZone,
        bool enabled,
        int maxConcurrent,
        int timeoutSeconds,
        DateTimeOffset now) : base(id)
    {
        ServiceId = serviceId;
        Name = name;
        Description = description;
        Command = command;
        CronExpression = cronExpression;
        TimeZone = timeZone;
        Enabled = enabled;
        MaxConcurrent = maxConcurrent;
        TimeoutSeconds = timeoutSeconds;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static ScheduledJob Create(
        ManagedServiceId serviceId,
        string name,
        string? description,
        string command,
        string cronExpression,
        string? timeZone,
        int? maxConcurrent,
        int? timeoutSeconds,
        DateTimeOffset now)
    {
        ValidateName(name);
        ValidateCommand(command);
        ValidateCron(cronExpression);
        var tz = NormalizeTimeZone(timeZone);
        var max = maxConcurrent.GetValueOrDefault(1);
        if (max < 1) { max = 1; }
        var timeout = timeoutSeconds.GetValueOrDefault(300);
        if (timeout <= 0) { timeout = 300; }
        return new ScheduledJob(
            ScheduledJobId.New(), serviceId, name.Trim(), description?.Trim(),
            command.Trim(), cronExpression.Trim(), tz, enabled: true, max, timeout, now);
    }

    public void UpdateDefinition(
        string? name,
        string? description,
        string? command,
        string? cronExpression,
        string? timeZone,
        int? maxConcurrent,
        int? timeoutSeconds,
        DateTimeOffset now)
    {
        if (name is not null)
        {
            ValidateName(name);
            Name = name.Trim();
        }
        if (description is not null)
        {
            Description = description.Trim();
        }
        if (command is not null)
        {
            ValidateCommand(command);
            Command = command.Trim();
        }
        if (cronExpression is not null)
        {
            ValidateCron(cronExpression);
            CronExpression = cronExpression.Trim();
            // Al cambiar la cron, invalidamos el proximo tick calculado.
            NextRunAt = null;
        }
        if (timeZone is not null)
        {
            TimeZone = NormalizeTimeZone(timeZone);
        }
        if (maxConcurrent is { } mc && mc >= 1)
        {
            MaxConcurrent = mc;
        }
        if (timeoutSeconds is { } ts && ts > 0)
        {
            TimeoutSeconds = ts;
        }
        UpdatedAt = now;
    }

    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAt = now;
        if (!enabled) { NextRunAt = null; }
    }

    public void MarkRun(DateTimeOffset startedAt, DateTimeOffset? nextRunAt)
    {
        LastRunAt = startedAt;
        NextRunAt = nextRunAt;
    }

    public void SetNextRunAt(DateTimeOffset? next)
    {
        NextRunAt = next;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name requerido.", nameof(name));
        }
        if (name.Trim().Length > 128)
        {
            throw new ArgumentException("Name no puede exceder 128 caracteres.", nameof(name));
        }
    }

    private static void ValidateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command requerido.", nameof(command));
        }
        if (command.Length > 2000)
        {
            throw new ArgumentException("Command no puede exceder 2000 caracteres.", nameof(command));
        }
    }

    private static void ValidateCron(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ArgumentException("CronExpression requerido.", nameof(cron));
        }
        if (cron.Trim().Length > 64)
        {
            throw new ArgumentException("CronExpression no puede exceder 64 caracteres.", nameof(cron));
        }
    }

    private static string NormalizeTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) { return "UTC"; }
        return timeZone.Trim();
    }

    // EF Core
    private ScheduledJob() : base()
    {
        Name = string.Empty;
        Command = string.Empty;
        CronExpression = string.Empty;
        TimeZone = "UTC";
    }
}
