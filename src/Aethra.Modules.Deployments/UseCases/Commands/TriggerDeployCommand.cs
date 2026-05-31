using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;

namespace Aethra.Modules.Deployments.UseCases.Commands;

/// <summary>
/// Encola un DeployJob para una Application + commit SHA específico.
/// Si <paramref name="GitSha"/> es null, el worker resolverá el HEAD del branch al clonar.
/// </summary>
public sealed record TriggerDeployCommand(
    string ApplicationId,
    string? GitSha,
    string Branch,
    DeployTrigger Trigger,
    string? TriggeredBy) : ICommand<DeployJobQueuedResult>;

public sealed record DeployJobQueuedResult(string JobId);

public sealed class TriggerDeployValidator : AbstractValidator<TriggerDeployCommand>
{
    public TriggerDeployValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.Branch).NotEmpty();
    }
}

internal sealed class TriggerDeployHandler(DeploymentsDbContext db, IClock clock, IDeployJobQueue queue)
    : ICommandHandler<TriggerDeployCommand, DeployJobQueuedResult>
{
    public async Task<Result<DeployJobQueuedResult>> Handle(TriggerDeployCommand request, CancellationToken ct)
    {
        // Si no nos pasaron sha (deploy manual sin commit específico), usamos un placeholder
        // que el worker resolverá al hacer clone.
        var sha = string.IsNullOrWhiteSpace(request.GitSha)
            ? "head"
            : request.GitSha.Trim().ToLowerInvariant();

        var job = DeployJob.Queue(request.ApplicationId, sha, request.Branch, request.Trigger,
            request.TriggeredBy, clock.UtcNow);
        db.DeployJobs.Add(job);
        await db.SaveChangesAsync(ct);

        // Notificar al worker (in-process channel).
        await queue.EnqueueAsync(job.Id, ct);

        _ = ct;
        return new DeployJobQueuedResult(job.Id.ToString());
    }
}

/// <summary>
/// Canal in-process del DeployWorker. Implementación concreta usa <c>Channel&lt;T&gt;</c>.
/// </summary>
public interface IDeployJobQueue
{
    ValueTask EnqueueAsync(DeployJobId jobId, CancellationToken ct);
    IAsyncEnumerable<DeployJobId> ReadAllAsync(CancellationToken ct);
}
