using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.Environments.Commands;

public sealed record CreateEnvironmentDefinitionCommand(
    string Slug,
    string DisplayName,
    int? Order) : ICommand<EnvironmentDefinitionDto>;

public sealed class CreateEnvironmentDefinitionValidator : AbstractValidator<CreateEnvironmentDefinitionCommand>
{
    public CreateEnvironmentDefinitionValidator()
    {
        RuleFor(c => c.Slug)
            .NotEmpty()
            .MaximumLength(32)
            .Matches("^[a-z][a-z0-9-]{0,30}[a-z0-9]$")
            .WithMessage("Slug debe ser lowercase alfanumérico con guiones (2-32 chars, sin guion al inicio/fin).");
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Order!.Value).GreaterThanOrEqualTo(0).When(c => c.Order.HasValue);
    }
}

internal sealed class CreateEnvironmentDefinitionHandler(SettingsDbContext db, IClock clock)
    : ICommandHandler<CreateEnvironmentDefinitionCommand, EnvironmentDefinitionDto>
{
    public async Task<Result<EnvironmentDefinitionDto>> Handle(
        CreateEnvironmentDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var normalized = request.Slug.Trim().ToLowerInvariant();
        if (await db.EnvironmentDefinitions.AnyAsync(e => e.Slug == normalized, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(
                "settings.environment_slug_taken",
                $"Ya existe un ambiente con slug '{normalized}'.");
        }

        var order = request.Order ?? await NextOrderAsync(cancellationToken).ConfigureAwait(false);

        EnvironmentDefinition env;
        try
        {
            env = EnvironmentDefinition.Create(request.Slug, request.DisplayName, order, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("settings.environment_invalid", ex.Message);
        }

        db.EnvironmentDefinitions.Add(env);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Mappers.ToDto(env);
    }

    private async Task<int> NextOrderAsync(CancellationToken ct)
    {
        var maxOrder = await db.EnvironmentDefinitions
            .AsNoTracking()
            .Select(e => (int?)e.Order)
            .MaxAsync(ct)
            .ConfigureAwait(false);
        return (maxOrder ?? -1) + 1;
    }
}
