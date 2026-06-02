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

public sealed record CreateUserCommand(
    string Email,
    string Password,
    string? DisplayName,
    IReadOnlyList<string> RoleSlugs) : ICommand<CreatedUserDto>;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8).MaximumLength(256);
        RuleFor(c => c.RoleSlugs)
            .NotNull()
            .Must(r => r.Count > 0)
            .WithMessage("Asigná al menos un rol.");
    }
}

internal sealed class CreateUserHandler(
    IdentityDbContext db,
    IUserRepository users,
    IRoleRepository roles,
    IUserPasswordCodec passwords,
    IClock clock) : ICommandHandler<CreateUserCommand, CreatedUserDto>
{
    public async Task<Result<CreatedUserDto>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = User.NormalizeEmail(request.Email);
        var existing = await users.FindByEmailAsync(normalizedEmail, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Error.Conflict("user.email_in_use", $"Ya existe un usuario con email '{normalizedEmail}'.");
        }

        // Resolvemos role slugs -> Role aggregates. Si alguno no existe el usuario obtiene
        // un error semántico antes de persistir.
        var requestedSlugs = request.RoleSlugs
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var resolvedRoles = new List<Role>(requestedSlugs.Count);
        foreach (var slug in requestedSlugs)
        {
            var role = await roles.FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
            if (role is null)
            {
                return Error.Validation("user.role_not_found", $"Rol '{slug}' no existe.");
            }
            resolvedRoles.Add(role);
        }

        byte[] cipher;
        try
        {
            cipher = passwords.HashAndProtect(request.Password);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("user.invalid_password", ex.Message);
        }

        User user;
        try
        {
            user = User.Create(request.Email, cipher, request.DisplayName, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("user.invalid", ex.Message);
        }

        foreach (var role in resolvedRoles)
        {
            user.AssignRole(role.Id, clock.UtcNow);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dto = new CreatedUserDto(
            Id: user.Id.ToString(),
            Email: user.Email,
            DisplayName: user.DisplayName,
            Roles: [.. resolvedRoles.Select(r => new RoleRefDto(r.Id.ToString(), r.Slug, r.DisplayName))]);

        return Result.Success(dto);
    }
}
