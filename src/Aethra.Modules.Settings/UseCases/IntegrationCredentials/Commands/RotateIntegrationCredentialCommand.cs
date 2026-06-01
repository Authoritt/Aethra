using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.IntegrationCredentials.Commands;

public sealed record RotateIntegrationCredentialCommand(string CredentialId, string NewPlainValue) : ICommand;

public sealed class RotateIntegrationCredentialValidator : AbstractValidator<RotateIntegrationCredentialCommand>
{
    public RotateIntegrationCredentialValidator()
    {
        RuleFor(c => c.CredentialId).NotEmpty();
        RuleFor(c => c.NewPlainValue).NotEmpty();
    }
}

internal sealed class RotateIntegrationCredentialHandler(
    SettingsDbContext db,
    IIntegrationCredentialCodec codec,
    IClock clock) : ICommandHandler<RotateIntegrationCredentialCommand>
{
    public async Task<Result> Handle(RotateIntegrationCredentialCommand request, CancellationToken cancellationToken)
    {
        var parsed = IdParsing.ParseCredentialId(request.CredentialId);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var credential = await db.IntegrationCredentials
            .FirstOrDefaultAsync(c => c.Id == parsed.Value, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return Error.NotFound("settings.credential_not_found", $"Credencial '{request.CredentialId}' no existe.");
        }

        try
        {
            credential.Rotate(request.NewPlainValue, codec, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("settings.credential_invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
