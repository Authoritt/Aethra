using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;

namespace Aethra.Modules.Identity.UseCases.Commands;

/// <summary>
/// Edita un rol custom (displayName + scopes). Los roles del sistema (IsSystem) no se pueden
/// modificar — el dominio lanza y devolvemos un conflict explícito.
/// </summary>
public sealed record UpdateRoleCommand(
    string RoleId,
    string DisplayName,
    IReadOnlyList<string> Scopes) : ICommand;

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Scopes).NotEmpty().WithMessage("Un rol requiere al menos un scope.");
    }
}

internal sealed class UpdateRoleHandler(
    IdentityDbContext db,
    IRoleRepository roles,
    IClock clock) : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.RoleId, out var parsed) || parsed.Value.Prefix != "rol")
        {
            return Error.NotFound("role.not_found", $"Role '{request.RoleId}' no existe.");
        }
        var typedId = new RoleId(parsed.Value);

        var role = await roles.GetByIdAsync(typedId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Error.NotFound("role.not_found", $"Role '{request.RoleId}' no existe.");
        }
        if (role.IsSystem)
        {
            return Error.Conflict("role.is_system", $"El rol '{role.Slug}' es del sistema y no puede modificarse.");
        }

        var now = clock.UtcNow;
        try
        {
            role.UpdateDisplayName(request.DisplayName, now);
            role.UpdateScopes(request.Scopes, now);
        }
        catch (InvalidOperationException)
        {
            // El dominio rechaza mutar roles del sistema (defensa adicional al guard de arriba).
            return Error.Conflict("role.is_system", $"El rol '{role.Slug}' es del sistema y no puede modificarse.");
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("role.invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
