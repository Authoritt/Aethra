using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Infrastructure.Deployment;
using Aethra.Modules.Deployments.UseCases.Deployment.Dtos;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Deployment.Commands;

/// <summary>
/// Promueve un deployment previamente completado a otra Instance, reutilizando la misma imagen
/// del Build origen. Es el patrón típico dev → staging → prod: el operador valida en dev y
/// luego "promueve" la misma versión a las Instances de los siguientes ambientes sin re-buildear.
///
/// <para>
/// Requiere que el deployment fuente esté en <see cref="DeploymentStatus.Completed"/> — promocionar
/// un fallido o cancelado no tiene sentido: la imagen pudo no haberse pusheado o haber sido
/// retirada. La Instance destino tiene que existir y puede ser cualquier Instance del mismo
/// Template (la UI valida en su flujo; aquí solo validamos existencia y referencia al mismo Build).
/// </para>
/// </summary>
public sealed record PromoteDeploymentCommand(
    string SourceDeploymentId,
    string TargetInstanceId,
    string? TriggeredBy) : ICommand<DeploymentSummaryDto>;

public sealed class PromoteDeploymentValidator : AbstractValidator<PromoteDeploymentCommand>
{
    public PromoteDeploymentValidator()
    {
        RuleFor(c => c.SourceDeploymentId).NotEmpty();
        RuleFor(c => c.TargetInstanceId).NotEmpty();
    }
}

internal sealed class PromoteDeploymentHandler(
    DeploymentsDbContext db,
    IInstanceLookup instances,
    IClock clock,
    IDeploymentJobQueue queue)
    : ICommandHandler<PromoteDeploymentCommand, DeploymentSummaryDto>
{
    public async Task<Result<DeploymentSummaryDto>> Handle(PromoteDeploymentCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.SourceDeploymentId, out var parsed) || parsed.Value.Prefix != "dep")
        {
            return Error.Validation("deployment.invalid_id",
                "ID de deployment fuente inválido.");
        }
        var typed = new DeploymentId(parsed.Value);

        var source = await db.Deployments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == typed, ct)
            .ConfigureAwait(false);
        if (source is null)
        {
            return Error.NotFound("deployment.not_found",
                $"Deployment fuente '{request.SourceDeploymentId}' no existe.");
        }
        if (source.Status != DeploymentStatus.Completed)
        {
            return Error.Conflict("deployment.source_not_completed",
                $"Solo se puede promover un deployment Completed; estado actual: {source.Status}.");
        }

        // No permitir promover a la misma Instance — sería un re-deploy, no una promoción.
        if (string.Equals(source.InstanceId, request.TargetInstanceId, StringComparison.Ordinal))
        {
            return Error.Validation("deployment.promote_self",
                "La Instance destino es la misma que la del deployment fuente.");
        }

        var target = await instances.GetByIdAsync(request.TargetInstanceId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Error.NotFound("deployment.instance_not_found",
                $"Instance destino '{request.TargetInstanceId}' no existe.");
        }

        var deployment = Domain.Deployment.Deployment.Queue(
            buildId: source.BuildId,
            instanceId: request.TargetInstanceId,
            newImageRef: source.NewImageRef,
            trigger: DeploymentTrigger.Promote,
            triggeredBy: request.TriggeredBy,
            now: clock.UtcNow);

        db.Deployments.Add(deployment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await queue.EnqueueAsync(deployment.Id, ct).ConfigureAwait(false);

        return DeploymentDtoMapper.ToSummary(deployment);
    }
}
