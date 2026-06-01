using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.BaseDomains.Commands;

public sealed record CreateBaseDomainCommand(string Hostname, string? CloudflareZoneId) : ICommand<BaseDomainDto>;

public sealed class CreateBaseDomainValidator : AbstractValidator<CreateBaseDomainCommand>
{
    public CreateBaseDomainValidator()
    {
        RuleFor(c => c.Hostname).NotEmpty().MaximumLength(253);
        RuleFor(c => c.CloudflareZoneId).MaximumLength(64);
    }
}

internal sealed class CreateBaseDomainHandler(SettingsDbContext db, IClock clock)
    : ICommandHandler<CreateBaseDomainCommand, BaseDomainDto>
{
    public async Task<Result<BaseDomainDto>> Handle(CreateBaseDomainCommand request, CancellationToken cancellationToken)
    {
        var normalized = request.Hostname.Trim().ToLowerInvariant();
        if (await db.BaseDomains.AnyAsync(d => d.Hostname == normalized, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(
                "settings.base_domain_taken",
                $"Ya existe un base domain con hostname '{normalized}'.");
        }

        BaseDomain domain;
        try
        {
            domain = BaseDomain.Create(request.Hostname, request.CloudflareZoneId, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("settings.base_domain_invalid", ex.Message);
        }

        db.BaseDomains.Add(domain);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Mappers.ToDto(domain);
    }
}
