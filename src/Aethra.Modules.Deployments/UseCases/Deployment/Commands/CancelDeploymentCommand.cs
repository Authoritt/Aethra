using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Deployment.Commands;

/// <summary>
/// Cancela un deployment en estado temprano (Pending/Pulling). Estados posteriores ya
/// implican contenedor levantándose y no son cancelables vía este comando: el operador
/// debería disparar un nuevo deploy con la imagen anterior si necesita revertir.
/// </summary>
public sealed record CancelDeploymentCommand(string DeploymentId) : ICommand;

public sealed class CancelDeploymentValidator : AbstractValidator<CancelDeploymentCommand>
{
    public CancelDeploymentValidator()
    {
        RuleFor(c => c.DeploymentId).NotEmpty();
    }
}

internal sealed class CancelDeploymentHandler(DeploymentsDbContext db, IClock clock)
    : ICommandHandler<CancelDeploymentCommand>
{
    public async Task<Result> Handle(CancelDeploymentCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.DeploymentId, out var parsed) || parsed.Value.Prefix != "dep")
        {
            return Error.Validation("deployment.invalid_id", "ID de deployment inválido.");
        }
        var typed = new DeploymentId(parsed.Value);

        var deployment = await db.Deployments.FirstOrDefaultAsync(d => d.Id == typed, ct)
            .ConfigureAwait(false);
        if (deployment is null)
        {
            return Error.NotFound("deployment.not_found",
                $"Deployment '{request.DeploymentId}' no existe.");
        }

        try
        {
            deployment.Cancel(clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Error.Conflict("deployment.not_cancellable", ex.Message);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
