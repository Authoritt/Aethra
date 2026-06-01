using System.Text.RegularExpressions;
using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Clients.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Clients.Commands;

/// <summary>
/// Crea un <c>Client</c> (tenant) dentro de un <c>Project</c>. <see cref="Slug"/> es único por
/// proyecto; la regex está enforced en el aggregate (<see cref="Client.Create"/>) — el validator
/// solo da feedback temprano antes de entrar al handler.
/// </summary>
public sealed record CreateClientCommand(
    string ProjectId,
    string Slug,
    string DisplayName,
    string? Description,
    string? ContactEmail,
    string? BillingTag) : ICommand<ClientDetail>;

public sealed partial class CreateClientValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientValidator()
    {
        RuleFor(c => c.ProjectId).NotEmpty();
        RuleFor(c => c.Slug)
            .NotEmpty()
            .MaximumLength(31)
            .Matches(ClientSlugRegex())
            .WithMessage(
                "Slug inválido. Debe empezar con letra minúscula, contener solo letras, dígitos o guion, y tener máximo 31 caracteres.");
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.ContactEmail).EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.ContactEmail));
        RuleFor(c => c.BillingTag).MaximumLength(64);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientSlugRegex();
}

internal sealed class CreateClientHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<CreateClientCommand, ClientDetail>
{
    public async Task<Result<ClientDetail>> Handle(CreateClientCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("client.invalid_project_id", "ID de proyecto inválido.");
        }
        var projectId = new ProjectId(parsed.Value);

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("client.project_not_found", $"Proyecto '{request.ProjectId}' no existe.");
        }

        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await db.Clients
                .AnyAsync(c => c.ProjectId == projectId && c.Slug == slug, cancellationToken)
                .ConfigureAwait(false))
        {
            return Error.Conflict(
                "client.slug_taken",
                $"Ya existe un client con slug '{slug}' en este proyecto.");
        }

        Client client;
        try
        {
            client = Client.Create(
                projectId,
                request.Slug,
                request.DisplayName,
                clock.UtcNow,
                request.Description,
                request.ContactEmail,
                request.BillingTag);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("client.invalid", ex.Message);
        }

        db.Clients.Add(client);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ClientDetail(
            id: client.Id.ToString(),
            projectId: client.ProjectId.ToString(),
            slug: client.Slug,
            displayName: client.DisplayName,
            description: client.Description,
            contactEmail: client.ContactEmail,
            billingTag: client.BillingTag,
            createdAt: client.CreatedAt,
            updatedAt: client.UpdatedAt);
    }
}
