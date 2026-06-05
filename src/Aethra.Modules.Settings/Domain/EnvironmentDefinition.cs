using System.Text.RegularExpressions;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Definición de un ambiente válido en Aethra (<c>production</c>, <c>staging</c>, <c>test</c>,
/// <c>preview</c>, etc.). No es un aggregate root: es una entidad simple gestionada de forma
/// plana — el resto del sistema solo necesita conocer la lista ordenada por <see cref="Order"/>.
/// </summary>
public sealed class EnvironmentDefinition : Entity<EnvironmentDefinitionId>
{
    // Slug minimalista: lowercase alfanumérico con guiones, sin dos puntos.
    private static readonly Regex SlugRegex = new(
        "^[a-z][a-z0-9-]{0,30}[a-z0-9]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public string Slug { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// Orden ascendente en la UI. El handler de <c>ReorderEnvironmentDefinitionsCommand</c>
    /// reasigna estos valores en bloque.
    /// </summary>
    public int Order { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private EnvironmentDefinition(
        EnvironmentDefinitionId id,
        string slug,
        string displayName,
        int order,
        DateTimeOffset createdAt) : base(id)
    {
        Slug = slug;
        DisplayName = displayName;
        Order = order;
        CreatedAt = createdAt;
    }

    public static EnvironmentDefinition Create(
        string slug,
        string displayName,
        int order,
        DateTimeOffset now)
    {
        ValidateSlug(slug);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("displayName no puede estar vacío.", nameof(displayName));
        }
        if (displayName.Trim().Length > 100)
        {
            throw new ArgumentException("displayName no puede exceder 100 caracteres.", nameof(displayName));
        }
        if (order < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "Order no puede ser negativo.");
        }

        return new EnvironmentDefinition(
            EnvironmentDefinitionId.New(),
            slug.Trim().ToLowerInvariant(),
            displayName.Trim(),
            order,
            now);
    }

    /// <summary>
    /// Actualiza la metadata editable (displayName). El slug es inmutable (identifica el ambiente)
    /// y el orden se gestiona aparte via <see cref="SetOrder"/> / bulk reorder.
    /// </summary>
    public void UpdateInfo(string displayName, DateTimeOffset now)
    {
        _ = now;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("displayName no puede estar vacío.", nameof(displayName));
        }
        if (displayName.Trim().Length > 100)
        {
            throw new ArgumentException("displayName no puede exceder 100 caracteres.", nameof(displayName));
        }
        DisplayName = displayName.Trim();
    }

    /// <summary>
    /// Actualiza el orden sin disparar eventos. Lo usa el bulk reorder.
    /// </summary>
    public void SetOrder(int newOrder)
    {
        if (newOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newOrder), "Order no puede ser negativo.");
        }
        Order = newOrder;
    }

    private static void ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Slug no puede estar vacío.", nameof(slug));
        }
        var normalized = slug.Trim().ToLowerInvariant();
        if (!SlugRegex.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Slug debe ser lowercase alfanumérico con guiones (2-32 chars, sin guion al inicio/fin).",
                nameof(slug));
        }
    }

    // EF Core
    private EnvironmentDefinition() : base()
    {
        Slug = string.Empty;
        DisplayName = string.Empty;
    }
}
