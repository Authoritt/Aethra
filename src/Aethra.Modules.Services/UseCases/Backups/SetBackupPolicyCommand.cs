using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Scheduling;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Backups;

public sealed record SetBackupPolicyCommand(
    string ServiceId,
    string? CronExpression,
    int? RetentionCount,
    string? Destination) : ICommand;

/// <summary>
/// Validación temprana de la backup policy (corre en el ValidationBehavior antes del handler). El
/// formato exacto del cron lo sigue verificando el handler con <c>CronExpression.TryParse</c>; acá
/// damos feedback inmediato de campos vacíos, retención fuera de rango y esquema de destino no
/// soportado (volume:// | s3:// | satellite://) — útil para atajar typos como <c>satelite://</c>.
/// </summary>
public sealed class SetBackupPolicyValidator : AbstractValidator<SetBackupPolicyCommand>
{
    private static readonly string[] AllowedSchemes = ["volume", "s3", "satellite"];

    public SetBackupPolicyValidator()
    {
        RuleFor(c => c.ServiceId).NotEmpty();
        RuleFor(c => c.CronExpression)
            .NotEmpty()
            .When(c => c.CronExpression is not null)
            .WithMessage("CronExpression no puede ser vacía (omítela para usar el default).");
        RuleFor(c => c.RetentionCount)
            .InclusiveBetween(1, 365)
            .When(c => c.RetentionCount is not null)
            .WithMessage("RetentionCount debe estar entre 1 y 365.");
        RuleFor(c => c.Destination)
            .Must(HasAllowedScheme)
            .When(c => !string.IsNullOrWhiteSpace(c.Destination))
            .WithMessage("Destination debe usar un esquema soportado: volume://, s3:// o satellite://.");
    }

    private static bool HasAllowedScheme(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return true;
        }
        var idx = destination.IndexOf("://", StringComparison.Ordinal);
        if (idx <= 0)
        {
            return false;
        }
        return AllowedSchemes.Contains(destination[..idx], StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class SetBackupPolicyHandler(ServicesDbContext db, IClock clock)
    : ICommandHandler<SetBackupPolicyCommand>
{
    public async Task<Result> Handle(SetBackupPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.Validation("service.invalid_id", $"ServiceId invalido: '{request.ServiceId}'.");
        }
        var id = new ManagedServiceId(parsed.Value);
        var svc = await db.ManagedServices.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"Servicio '{request.ServiceId}' no existe.");
        }

        // Si todos los campos son null → desactivar policy.
        BackupPolicy? policy = null;
        if (request.CronExpression is not null
            || request.RetentionCount is not null
            || request.Destination is not null)
        {
            policy = new BackupPolicy(
                CronExpression: request.CronExpression ?? "0 2 * * *",
                RetentionCount: request.RetentionCount ?? 7,
                Destination: request.Destination ?? "volume://default");

            // BackupPolicy.IsValid() sólo exige cron NO vacío; pero un cron malformado se persistiría
            // y el BackupWorker nunca dispararía la policy (backups rotos en silencio). Validamos el
            // FORMATO acá con el mismo parser de 5 campos que usa CreateScheduledJobCommand.
            if (!CronExpression.TryParse(policy.CronExpression, out _))
            {
                return Error.Validation("backup.invalid_cron",
                    $"CronExpression invalida: '{policy.CronExpression}'. Formato: 'minute hour day month dow' (ej. '0 2 * * *').");
            }
        }

        try
        {
            svc.SetBackupPolicy(policy, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("backup.policy_invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
