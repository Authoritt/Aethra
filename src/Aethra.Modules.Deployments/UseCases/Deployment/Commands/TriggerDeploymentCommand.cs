using Aethra.Modules.Deployments.Domain.Build;
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
/// Manual override: encola un <see cref="Domain.Deployment.Deployment"/> para un Build + Instance
/// específicos. A diferencia del fan-out automático (<c>BuildCompletedHandler</c>) este comando
/// permite redesplegar una imagen ya construida sobre cualquier Instance, ignorando la flag
/// <c>AutoDeployOnNewBuild</c>.
/// </summary>
public sealed record TriggerDeploymentCommand(
    string BuildId,
    string InstanceId,
    string? TriggeredBy) : ICommand<DeploymentSummaryDto>;

public sealed class TriggerDeploymentValidator : AbstractValidator<TriggerDeploymentCommand>
{
    public TriggerDeploymentValidator()
    {
        RuleFor(c => c.BuildId).NotEmpty();
        RuleFor(c => c.InstanceId).NotEmpty();
    }
}

internal sealed class TriggerDeploymentHandler(
    DeploymentsDbContext db,
    IInstanceLookup instances,
    IClock clock,
    IDeploymentJobQueue queue)
    : ICommandHandler<TriggerDeploymentCommand, DeploymentSummaryDto>
{
    public async Task<Result<DeploymentSummaryDto>> Handle(TriggerDeploymentCommand request, CancellationToken ct)
    {
        // Validar Build existe y es exitoso (sin ImageRef no hay nada que desplegar).
        if (!AethraId.TryParse(request.BuildId, out var buildAetherId) || buildAetherId.Value.Prefix != "bld")
        {
            return Error.Validation("deployment.invalid_build_id", "ID de build inválido.");
        }
        var typedBuildId = new BuildId(buildAetherId.Value);
        var build = await db.Builds.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == typedBuildId, ct)
            .ConfigureAwait(false);
        if (build is null)
        {
            return Error.NotFound("deployment.build_not_found",
                $"Build '{request.BuildId}' no existe.");
        }
        if (build.Status != BuildStatus.Completed || string.IsNullOrWhiteSpace(build.ImageRef))
        {
            return Error.Conflict("deployment.build_not_ready",
                $"Build '{request.BuildId}' no completó con éxito (status={build.Status}).");
        }

        // Validar Instance existe.
        var instance = await instances.GetByIdAsync(request.InstanceId, ct).ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("deployment.instance_not_found",
                $"Instance '{request.InstanceId}' no existe.");
        }

        var deployment = Domain.Deployment.Deployment.Queue(
            buildId: request.BuildId,
            instanceId: request.InstanceId,
            newImageRef: build.ImageRef,
            trigger: DeploymentTrigger.Manual,
            triggeredBy: request.TriggeredBy,
            now: clock.UtcNow);

        db.Deployments.Add(deployment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await queue.EnqueueAsync(deployment.Id, ct).ConfigureAwait(false);

        return DeploymentDtoMapper.ToSummary(deployment);
    }
}
