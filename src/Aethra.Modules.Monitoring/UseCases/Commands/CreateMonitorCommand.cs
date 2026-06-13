using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Commands;

public sealed record CreateMonitorCommand(
    string Slug,
    string Name,
    string Url,
    string HttpMethod,
    IReadOnlyList<int>? ExpectedStatusCodes,
    int? IntervalSec,
    int? TimeoutMs,
    IReadOnlyDictionary<string, string>? Headers,
    string? BodyTemplate,
    string? InstanceId,
    string? ProjectId) : ICommand<MonitorDetailDto>;

public sealed class CreateMonitorValidator : AbstractValidator<CreateMonitorCommand>
{
    public CreateMonitorValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Url).NotEmpty().MaximumLength(2048);
        RuleFor(c => c.HttpMethod).NotEmpty();
        // Códigos fuera de 100–599 hoy se descartan en silencio (Monitor.NormalizeExpected); rechazarlos
        // explícitamente evita que un typo (ej. 2000) cambie el comportamiento del monitor sin aviso.
        RuleForEach(c => c.ExpectedStatusCodes).InclusiveBetween(100, 599);
    }
}

internal sealed class CreateMonitorHandler(MonitoringDbContext db, IClock clock)
    : ICommandHandler<CreateMonitorCommand, MonitorDetailDto>
{
    public async Task<Result<MonitorDetailDto>> Handle(CreateMonitorCommand request, CancellationToken ct)
    {
        var slugResult = Slug.Create(request.Slug);
        if (slugResult.IsFailure)
        {
            return slugResult.Error;
        }
        var slug = slugResult.Value;

        if (!Enum.TryParse<MonitorHttpMethod>(request.HttpMethod, ignoreCase: true, out var method))
        {
            return Error.Validation("monitor.invalid_method", "HttpMethod debe ser GET, HEAD o POST.");
        }

        if (await db.Monitors.AnyAsync(m => m.Slug == slug, ct).ConfigureAwait(false))
        {
            return Error.Conflict("monitor.slug_taken", $"Ya existe un monitor con slug '{slug}'.");
        }

        Monitor monitor;
        try
        {
            monitor = Monitor.Create(
                slug,
                request.Name,
                request.Url,
                method,
                request.ExpectedStatusCodes ?? [200],
                request.IntervalSec ?? 60,
                request.TimeoutMs ?? 10000,
                request.Headers,
                request.BodyTemplate,
                request.InstanceId,
                request.ProjectId,
                clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("monitor.invalid_config", ex.Message);
        }

        db.Monitors.Add(monitor);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return MonitorMapper.ToDetail(monitor);
    }
}
