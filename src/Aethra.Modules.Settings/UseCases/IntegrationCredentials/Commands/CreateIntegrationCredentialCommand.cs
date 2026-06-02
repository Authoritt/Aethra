using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.IntegrationCredentials.Commands;

/// <summary>
/// Crea una nueva credencial externa. El <paramref name="PlainValue"/> se cifra en el
/// aggregate; no se persiste en plain en ningún momento. La respuesta NO incluye el valor.
/// </summary>
public sealed record CreateIntegrationCredentialCommand(
    string Name,
    IntegrationCredentialType Type,
    string DisplayName,
    string PlainValue,
    IReadOnlyDictionary<string, string>? Metadata,
    string? Description) : ICommand<IntegrationCredentialDto>;

public sealed class CreateIntegrationCredentialValidator : AbstractValidator<CreateIntegrationCredentialCommand>
{
    public CreateIntegrationCredentialValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z]+:[a-z0-9-]+$")
            .WithMessage("El nombre debe seguir el formato 'namespace:slug' (lowercase, alfanumérico y guiones).");
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.PlainValue).NotEmpty();
        RuleFor(c => c.Description).MaximumLength(500);
    }
}

internal sealed class CreateIntegrationCredentialHandler(
    SettingsDbContext db,
    IIntegrationCredentialCodec codec,
    IClock clock) : ICommandHandler<CreateIntegrationCredentialCommand, IntegrationCredentialDto>
{
    public async Task<Result<IntegrationCredentialDto>> Handle(
        CreateIntegrationCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var normalized = request.Name.Trim().ToLowerInvariant();
        if (await db.IntegrationCredentials.AnyAsync(c => c.Name == normalized, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(
                "settings.credential_name_taken",
                $"Ya existe una credencial con el nombre '{normalized}'.");
        }

        IntegrationCredential credential;
        try
        {
            credential = IntegrationCredential.Create(
                request.Name,
                request.Type,
                request.DisplayName,
                request.PlainValue,
                request.Metadata,
                codec,
                clock.UtcNow,
                request.Description);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("settings.credential_invalid", ex.Message);
        }

        db.IntegrationCredentials.Add(credential);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            // Race condition: otro request paso el AnyAsync antes de SaveChanges y la BD ahora
            // rechaza por UNIQUE constraint. Reportamos el mismo conflict que el chequeo previo
            // para mantener consistencia de codigo de error frente al cliente.
            return Error.Conflict(
                "settings.credential_name_taken",
                $"Ya existe una credencial con el nombre '{normalized}'.");
        }

        return Mappers.ToDto(credential);
    }
}
