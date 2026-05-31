using System.Text.RegularExpressions;
using Aethra.Modules.Projects.Domain.Clients.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Clients;

/// <summary>
/// Tenant lógico dentro de un Project (Empresa A, Empresa B, etc.). Un Client puede tener
/// <em>Instances</em> de múltiples Templates dentro del mismo Project — no está acoplado a un
/// Template concreto.
///
/// El <see cref="Slug"/> es único por Project y se usa para componer nombres deterministas de
/// contenedores y volúmenes (<c>{template.slug}-{client.slug}-{environment}</c>).
/// </summary>
public sealed partial class Client : AggregateRoot<ClientId>
{
    public ProjectId ProjectId { get; private set; }

    /// <summary>
    /// Slug del tenant. Más restrictivo que <c>Aethra.Shared.Kernel.Primitives.Slug</c>:
    /// regex <c>^[a-z][a-z0-9-]{0,30}$</c> — debe empezar con letra, máximo 31 caracteres.
    /// Esto garantiza que los nombres compuestos de contenedor caben en el límite Docker.
    /// </summary>
    public string Slug { get; private set; }

    public string DisplayName { get; private set; }
    public string? Description { get; private set; }
    public string? ContactEmail { get; private set; }

    /// <summary>
    /// Tag opcional para reporting de facturación / atribución de costos.
    /// Opaco al dominio; solo se persiste y expone.
    /// </summary>
    public string? BillingTag { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Client(
        ClientId id,
        ProjectId projectId,
        string slug,
        string displayName,
        DateTimeOffset now) : base(id)
    {
        ProjectId = projectId;
        Slug = slug;
        DisplayName = displayName;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Client Create(
        ProjectId projectId,
        string slug,
        string displayName,
        DateTimeOffset now,
        string? description = null,
        string? contactEmail = null,
        string? billingTag = null)
    {
        var normalizedSlug = NormalizeSlug(slug);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("El display name no puede estar vacío.", nameof(displayName));
        }

        var client = new Client(ClientId.New(), projectId, normalizedSlug, displayName.Trim(), now)
        {
            Description = description?.Trim(),
            ContactEmail = NormalizeEmail(contactEmail),
            BillingTag = billingTag?.Trim(),
        };
        client.Raise(new ClientCreatedEvent(client.Id, projectId, normalizedSlug, client.DisplayName));
        return client;
    }

    /// <summary>
    /// Actualiza información administrativa del tenant. El <see cref="Slug"/> NO se puede cambiar
    /// (rompería referencias en container names ya desplegados).
    /// </summary>
    public void UpdateInfo(
        string displayName,
        string? description,
        string? contactEmail,
        string? billingTag,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("El display name no puede estar vacío.", nameof(displayName));
        }
        DisplayName = displayName.Trim();
        Description = description?.Trim();
        ContactEmail = NormalizeEmail(contactEmail);
        BillingTag = billingTag?.Trim();
        UpdatedAt = now;
        Raise(new ClientInfoUpdatedEvent(Id, DisplayName));
    }

    private static string NormalizeSlug(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("El slug del client no puede estar vacío.", nameof(input));
        }
        var trimmed = input.Trim().ToLowerInvariant();
        if (!ClientSlugRegex().IsMatch(trimmed))
        {
            throw new ArgumentException(
                "Slug inválido. Debe empezar con letra minúscula, contener solo letras, dígitos o guion, " +
                "y tener máximo 31 caracteres.",
                nameof(input));
        }
        return trimmed;
    }

    private static string? NormalizeEmail(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }
        return input.Trim().ToLowerInvariant();
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientSlugRegex();

    // EF Core
    private Client() : base()
    {
        Slug = string.Empty;
        DisplayName = string.Empty;
    }
}
