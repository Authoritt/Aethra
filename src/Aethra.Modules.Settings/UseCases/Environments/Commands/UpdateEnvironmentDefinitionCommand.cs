using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.Environments.Commands;

/// <summary>
/// Actualiza el displayName de un ambiente. El slug es inmutable (identifica el ambiente).
/// </summary>
public sealed record UpdateEnvironmentDefinitionCommand(
    string EnvironmentId,
    string DisplayName) : ICommand<EnvironmentDefinitionDto>;

public sealed class UpdateEnvironmentDefinitionValidator : AbstractValidator<UpdateEnvironmentDefinitionCommand>
{
    public UpdateEnvironmentDefinitionValidator()
    {
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(100);
    }
}

internal sealed class UpdateEnvironmentDefinitionHandler(SettingsDbContext db, IClock clock)
    : ICommandHandler<UpdateEnvironmentDefinitionCommand, EnvironmentDefinitionDto>
{
    public async Task<Result<EnvironmentDefinitionDto>> Handle(
        UpdateEnvironmentDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var parsed = IdParsing.ParseEnvironmentDefinitionId(request.EnvironmentId);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var env = await db.EnvironmentDefinitions
            .FirstOrDefaultAsync(e => e.Id == parsed.Value, cancellationToken)
            .ConfigureAwait(false);
        if (env is null)
        {
            return Error.NotFound("settings.environment_not_found", $"Ambiente '{request.EnvironmentId}' no existe.");
        }

        try
        {
            env.UpdateInfo(request.DisplayName, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("settings.environment_invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Mappers.ToDto(env);
    }
}
