using System.Text.RegularExpressions;
using Aethra.Modules.Settings.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Base domain (FQDN) bajo el cual Aethra crea hostnames para los recursos administrados
/// (ej. <c>*.aethra.example.com</c>). Solo una instancia puede estar activa a la vez —
/// <see cref="Activate"/> es el método que aplica esa invariante a nivel de proceso,
/// y el handler de comando se asegura de desactivar las demás.
///
/// <see cref="WildcardConfigured"/> es un flag manual del operador: confirma que el
/// registro DNS comodín está activo. Aethra no lo verifica automáticamente (eso se hace
/// con sintetic checks fuera de este aggregate).
/// </summary>
public sealed class BaseDomain : AggregateRoot<BaseDomainId>
{
    // FQDN simplificado: labels alfanuméricas separadas por puntos, max 253 chars en total.
    private static readonly Regex FqdnRegex = new(
        @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public string Hostname { get; private set; }

    /// <summary>
    /// Referencia opcional al aggregate <c>CloudflareZone</c>. Es <c>string?</c> y no un
    /// <c>CloudflareZoneId</c> tipado porque Settings no debe referenciar internals de
    /// <c>Modules.Cloudflare</c> (regla de aislamiento entre módulos).
    /// </summary>
    public string? CloudflareZoneId { get; private set; }

    public bool WildcardConfigured { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BaseDomain(
        BaseDomainId id,
        string hostname,
        string? cloudflareZoneId,
        DateTimeOffset now) : base(id)
    {
        Hostname = hostname;
        CloudflareZoneId = cloudflareZoneId;
        WildcardConfigured = false;
        IsActive = false;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static BaseDomain Create(
        string hostname,
        string? cloudflareZoneId,
        DateTimeOffset now)
    {
        ValidateHostname(hostname);
        var normalized = hostname.Trim().ToLowerInvariant();
        var zoneId = string.IsNullOrWhiteSpace(cloudflareZoneId) ? null : cloudflareZoneId.Trim();

        var domain = new BaseDomain(BaseDomainId.New(), normalized, zoneId, now);
        domain.Raise(new BaseDomainCreatedEvent(domain.Id, domain.Hostname));
        return domain;
    }

    /// <summary>
    /// Marca este base domain como activo. El handler debe primero desactivar los demás
    /// para no romper la invariante "solo uno activo".
    /// </summary>
    public void Activate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return;
        }
        IsActive = true;
        UpdatedAt = now;
        Raise(new BaseDomainActivatedEvent(Id, Hostname));
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }
        IsActive = false;
        UpdatedAt = now;
    }

    public void MarkWildcardConfigured(DateTimeOffset now)
    {
        if (WildcardConfigured)
        {
            return;
        }
        WildcardConfigured = true;
        UpdatedAt = now;
    }

    public void LinkCloudflareZone(string? cloudflareZoneId, DateTimeOffset now)
    {
        CloudflareZoneId = string.IsNullOrWhiteSpace(cloudflareZoneId) ? null : cloudflareZoneId.Trim();
        UpdatedAt = now;
    }

    private static void ValidateHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new ArgumentException("Hostname no puede estar vacío.", nameof(hostname));
        }
        var normalized = hostname.Trim().ToLowerInvariant();
        if (normalized.Length > 253)
        {
            throw new ArgumentException("Hostname no puede exceder 253 caracteres.", nameof(hostname));
        }
        if (!FqdnRegex.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Hostname debe ser un FQDN válido (lowercase, mínimo dos labels, sin guiones al inicio/fin).",
                nameof(hostname));
        }
    }

    // EF Core
    private BaseDomain() : base()
    {
        Hostname = string.Empty;
    }
}
