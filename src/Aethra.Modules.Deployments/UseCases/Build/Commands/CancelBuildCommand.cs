using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Build.Commands;

/// <summary>
/// Cancela un build que aún esté en fase temprana (Queued/Cloning/Building). Una vez en
/// Pushing o estado terminal, la operación devuelve <see cref="ErrorType.Conflict"/>.
/// </summary>
public sealed record CancelBuildCommand(string BuildId) : ICommand;

public sealed class CancelBuildValidator : AbstractValidator<CancelBuildCommand>
{
    public CancelBuildValidator()
    {
        RuleFor(c => c.BuildId).NotEmpty();
    }
}

internal sealed class CancelBuildHandler(DeploymentsDbContext db, IClock clock)
    : ICommandHandler<CancelBuildCommand>
{
    public async Task<Result> Handle(CancelBuildCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.BuildId, out var parsed) || parsed.Value.Prefix != "bld")
        {
            return Error.Validation("build.invalid_id", "ID de build inválido.");
        }
        var typed = new BuildId(parsed.Value);

        var build = await db.Builds.FirstOrDefaultAsync(b => b.Id == typed, ct).ConfigureAwait(false);
        if (build is null)
        {
            return Error.NotFound("build.not_found", $"Build '{request.BuildId}' no existe.");
        }

        try
        {
            build.Cancel(clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Error.Conflict("build.not_cancellable", ex.Message);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
