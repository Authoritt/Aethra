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

public sealed record RollbackDeploymentCommand(
    string SourceDeploymentId,
    string? TriggeredBy) : ICommand<DeploymentSummaryDto>;

public sealed class RollbackDeploymentValidator : AbstractValidator<RollbackDeploymentCommand>
{
    public RollbackDeploymentValidator()
    {
        RuleFor(c => c.SourceDeploymentId).NotEmpty();
    }
}

internal sealed class RollbackDeploymentHandler(
    DeploymentsDbContext db,
    IInstanceLookup instances,
    IClock clock,
    IDeploymentJobQueue queue)
    : ICommandHandler<RollbackDeploymentCommand, DeploymentSummaryDto>
{
    public async Task<Result<DeploymentSummaryDto>> Handle(RollbackDeploymentCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.SourceDeploymentId, out var parsed) || parsed.Value.Prefix != "dep")
        {
            return Error.Validation("deployment.invalid_id", "ID de deployment fuente invalido.");
        }

        var sourceId = new DeploymentId(parsed.Value);
        var source = await db.Deployments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == sourceId, ct)
            .ConfigureAwait(false);
        if (source is null)
        {
            return Error.NotFound("deployment.not_found",
                $"Deployment fuente '{request.SourceDeploymentId}' no existe.");
        }
        if (source.Status != DeploymentStatus.Completed)
        {
            return Error.Conflict("deployment.source_not_completed",
                $"Solo se puede rollbackear hacia un deployment Completed; estado actual: {source.Status}.");
        }

        var instance = await instances.GetByIdAsync(source.InstanceId, ct).ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("deployment.instance_not_found",
                $"Instance destino '{source.InstanceId}' no existe.");
        }

        var deployment = Domain.Deployment.Deployment.Queue(
            buildId: source.BuildId,
            instanceId: source.InstanceId,
            newImageRef: source.NewImageRef,
            trigger: DeploymentTrigger.Rollback,
            triggeredBy: request.TriggeredBy,
            now: clock.UtcNow);

        db.Deployments.Add(deployment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await queue.EnqueueAsync(deployment.Id, ct).ConfigureAwait(false);

        return DeploymentDtoMapper.ToSummary(deployment);
    }
}
