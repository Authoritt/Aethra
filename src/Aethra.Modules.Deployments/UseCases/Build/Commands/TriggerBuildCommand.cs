using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Infrastructure.Build;
using Aethra.Modules.Deployments.UseCases.Build.Dtos;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;

namespace Aethra.Modules.Deployments.UseCases.Build.Commands;

/// <summary>
/// Encola un build para un Template + commit SHA específico. Valida que el Template existe
/// (via <see cref="ITemplateLookup"/>) antes de persistir; un commit "head" sin SHA real es
/// rechazado para evitar builds ambiguos.
/// </summary>
public sealed record TriggerBuildCommand(
    string TemplateId,
    string GitSha,
    string GitRef,
    BuildTrigger Trigger,
    string? TriggeredBy) : ICommand<BuildSummaryDto>;

public sealed class TriggerBuildValidator : AbstractValidator<TriggerBuildCommand>
{
    public TriggerBuildValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.GitSha).NotEmpty().MinimumLength(7);
        RuleFor(c => c.GitRef).NotEmpty();
    }
}

internal sealed class TriggerBuildHandler(
    DeploymentsDbContext db,
    ITemplateLookup templates,
    IClock clock,
    IBuildJobQueue queue)
    : ICommandHandler<TriggerBuildCommand, BuildSummaryDto>
{
    public async Task<Result<BuildSummaryDto>> Handle(TriggerBuildCommand request, CancellationToken ct)
    {
        var template = await templates.GetByIdAsync(request.TemplateId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("build.template_not_found",
                $"Template '{request.TemplateId}' no existe.");
        }

        var build = Domain.Build.Build.Queue(
            template.TemplateId,
            request.GitSha,
            request.GitRef,
            request.Trigger,
            request.TriggeredBy,
            clock.UtcNow);

        db.Builds.Add(build);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Notificar al worker (channel in-process). Si el worker no levantó todavía, el
        // build queda en BD con status=Queued y el recovery host lo retomará (F9.3.5).
        await queue.EnqueueAsync(build.Id, ct).ConfigureAwait(false);

        return BuildDtoMapper.ToSummary(build);
    }
}
