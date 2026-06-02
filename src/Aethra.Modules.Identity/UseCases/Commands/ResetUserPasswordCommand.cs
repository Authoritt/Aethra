using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;

namespace Aethra.Modules.Identity.UseCases.Commands;

/// <summary>
/// Reset administrativo de password. F11.1 NO envía email — el caller asume responsabilidad
/// de comunicar el nuevo password al usuario por canal seguro. F11.3 conectará SMTP.
/// </summary>
public sealed record ResetUserPasswordCommand(string UserId, string NewPassword)
    : ICommand<ResetPasswordResultDto>;

public sealed class ResetUserPasswordValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordValidator()
    {
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(256);
    }
}

internal sealed class ResetUserPasswordHandler(
    IdentityDbContext db,
    IUserRepository users,
    IUserPasswordCodec passwords,
    IClock clock) : ICommandHandler<ResetUserPasswordCommand, ResetPasswordResultDto>
{
    public async Task<Result<ResetPasswordResultDto>> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.UserId, out var parsed) || parsed.Value.Prefix != "usr")
        {
            return Error.NotFound("user.not_found", $"User '{request.UserId}' no existe.");
        }
        var typedId = new UserId(parsed.Value);

        var user = await users.GetByIdAsync(typedId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("user.not_found", $"User '{request.UserId}' no existe.");
        }

        byte[] cipher;
        try
        {
            cipher = passwords.HashAndProtect(request.NewPassword);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("user.invalid_password", ex.Message);
        }

        user.ResetPassword(cipher, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new ResetPasswordResultDto(user.Id.ToString(), user.Email));
    }
}
