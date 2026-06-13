using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using FluentValidation;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Commands;

/// <summary>
/// Patch parcial sobre un monitor. Cualquier campo null se ignora, salvo los <c>ClearX</c>
/// que permiten poner el campo a null explícitamente (necesario para PATCH semántico real:
/// nullable significa "no quiero tocar", clear significa "quiero borrar").
/// </summary>
public sealed record UpdateMonitorCommand(
    string MonitorId,
    string? Name,
    string? Url,
    string? HttpMethod,
    IReadOnlyList<int>? ExpectedStatusCodes,
    int? IntervalSec,
    int? TimeoutMs,
    IReadOnlyDictionary<string, string>? Headers,
    bool ClearHeaders,
    string? BodyTemplate,
    bool ClearBodyTemplate,
    string? InstanceId,
    bool ClearInstanceId,
    string? ProjectId,
    bool ClearProjectId) : ICommand<MonitorDetailDto>;

/// <summary>
/// Validación de patch: corre en el ValidationBehavior antes del handler. Semántica PATCH — sólo
/// valida los campos PROVISTOS (no-null), espejando las reglas de <c>CreateMonitorValidator</c>
/// (Name/Url/HttpMethod) para que un update no pueda dejar un monitor en un estado que create rechazaría.
/// </summary>
public sealed class UpdateMonitorValidator : AbstractValidator<UpdateMonitorCommand>
{
    public UpdateMonitorValidator()
    {
        RuleFor(c => c.MonitorId).NotEmpty();
        When(c => c.Name is not null, () =>
            RuleFor(c => c.Name).NotEmpty().MaximumLength(255));
        When(c => c.Url is not null, () =>
            RuleFor(c => c.Url).NotEmpty().MaximumLength(2048));
        When(c => c.HttpMethod is not null, () =>
            RuleFor(c => c.HttpMethod).NotEmpty());
    }
}

internal sealed class UpdateMonitorHandler(MonitoringDbContext db, IClock clock)
    : ICommandHandler<UpdateMonitorCommand, MonitorDetailDto>
{
    public async Task<Result<MonitorDetailDto>> Handle(UpdateMonitorCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.MonitorId, out var parsed) || parsed.Value.Prefix != "mon")
        {
            return Error.Validation("monitor.invalid_id", "ID de monitor inválido.");
        }
        var typedId = new MonitorId(parsed.Value);

        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == typedId, ct).ConfigureAwait(false);
        if (monitor is null)
        {
            return Error.NotFound("monitor.not_found", $"Monitor '{request.MonitorId}' no existe.");
        }

        MonitorHttpMethod? method = null;
        if (!string.IsNullOrEmpty(request.HttpMethod))
        {
            if (!Enum.TryParse<MonitorHttpMethod>(request.HttpMethod, ignoreCase: true, out var m))
            {
                return Error.Validation("monitor.invalid_method", "HttpMethod debe ser GET, HEAD o POST.");
            }
            method = m;
        }

        try
        {
            monitor.UpdateConfig(
                request.Name,
                request.Url,
                method,
                request.ExpectedStatusCodes,
                request.IntervalSec,
                request.TimeoutMs,
                request.Headers,
                request.ClearHeaders,
                request.BodyTemplate,
                request.ClearBodyTemplate,
                request.InstanceId,
                request.ClearInstanceId,
                request.ProjectId,
                request.ClearProjectId,
                clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("monitor.invalid_config", ex.Message);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MonitorMapper.ToDetail(monitor);
    }
}
