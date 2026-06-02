using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;

namespace Aethra.Modules.Identity.UseCases.Commands;

public sealed record CreateRoleCommand(
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Scopes) : ICommand<CreatedRoleDto>;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().MaximumLength(64);
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Scopes).NotEmpty().WithMessage("Un rol requiere al menos un scope.");
    }
}

internal sealed class CreateRoleHandler(
    IdentityDbContext db,
    IRoleRepository roles,
    IClock clock) : ICommandHandler<CreateRoleCommand, CreatedRoleDto>
{
    public async Task<Result<CreatedRoleDto>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
        var existing = await roles.FindBySlugAsync(normalizedSlug, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Error.Conflict("role.slug_in_use", $"Ya existe un rol con slug '{normalizedSlug}'.");
        }
        if (Role.SystemSlugs.Contains(normalizedSlug))
        {
            return Error.Conflict("role.system_slug", $"El slug '{normalizedSlug}' está reservado para roles del sistema.");
        }

        Role role;
        try
        {
            role = Role.CreateCustom(request.Slug, request.DisplayName, request.Scopes, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("role.invalid", ex.Message);
        }

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new CreatedRoleDto(
            Id: role.Id.ToString(),
            Slug: role.Slug,
            DisplayName: role.DisplayName,
            Scopes: [.. role.Scopes]));
    }
}
