using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Cloudflare.Domain;

/// <summary>
/// F13.9 — un Cloudflare Tunnel gestionado de forma REMOTA por Aethra (config de ingress vía API,
/// no archivo local). Permite agregar/quitar reglas de ingress por hostname SIN reiniciar cloudflared
/// (cero blip). Aethra guarda el token API cifrado (DataProtection, mismo purpose que las zonas) para
/// llamar al endpoint <c>/accounts/{acct}/cfd_tunnel/{id}/configurations</c>.
///
/// Convención de servicios: los hosts servidos por Aethra apuntan a <see cref="AethraService"/>
/// (el proxy YARP del central, ej. <c>http://localhost:5080</c>); la regla catch-all final apunta a
/// <see cref="FallbackService"/> (ej. Traefik <c>https://localhost:443</c> con noTLSVerify).
/// </summary>
public sealed class CloudflareTunnel : AggregateRoot<CloudflareTunnelId>
{
    /// <summary>UUID externo del túnel en Cloudflare (usado en la URL del API).</summary>
    public string TunnelId { get; private set; }

    /// <summary>Nombre humano del túnel (ej. <c>authorit-apps</c>).</summary>
    public string Name { get; private set; }

    /// <summary>Account id de Cloudflare dueño del túnel.</summary>
    public string AccountId { get; private set; }

    /// <summary>Token API cifrado (scope <c>Cloudflare Tunnel:Edit</c>). Nunca devolver crudo.</summary>
    public byte[] ApiTokenCipher { get; private set; }

    /// <summary>Servicio al que apuntan los hosts gestionados por Aethra (proxy del central).</summary>
    public string AethraService { get; private set; }

    /// <summary>Servicio de la regla catch-all final (ej. Traefik). Vacío = http_status:404.</summary>
    public string FallbackService { get; private set; }

    public bool FallbackNoTlsVerify { get; private set; }

    /// <summary>VM (satélite) donde corre el connector de este túnel. El connector DEBE co-ubicarse con
    /// los servicios upstream (localhost). Null = no asignada (deploy de connector requiere fijarla).</summary>
    public string? TargetVmId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    private CloudflareTunnel(
        CloudflareTunnelId id, string tunnelId, string name, string accountId, byte[] apiTokenCipher,
        string aethraService, string fallbackService, bool fallbackNoTlsVerify, DateTimeOffset now) : base(id)
    {
        TunnelId = tunnelId;
        Name = name;
        AccountId = accountId;
        ApiTokenCipher = apiTokenCipher;
        AethraService = aethraService;
        FallbackService = fallbackService;
        FallbackNoTlsVerify = fallbackNoTlsVerify;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static CloudflareTunnel Create(
        string tunnelId, string name, string accountId, byte[] apiTokenCipher,
        string? aethraService, string? fallbackService, bool fallbackNoTlsVerify, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tunnelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(apiTokenCipher);
        if (apiTokenCipher.Length == 0)
        {
            throw new ArgumentException("apiTokenCipher no puede estar vacío.", nameof(apiTokenCipher));
        }

        return new CloudflareTunnel(
            CloudflareTunnelId.New(), tunnelId.Trim(), name.Trim(), accountId.Trim(), apiTokenCipher,
            string.IsNullOrWhiteSpace(aethraService) ? "http://localhost:5080" : aethraService.Trim(),
            string.IsNullOrWhiteSpace(fallbackService) ? "https://localhost:443" : fallbackService.Trim(),
            fallbackNoTlsVerify, now);
    }

    public void UpdateToken(byte[] newCipher, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newCipher);
        if (newCipher.Length == 0)
        {
            throw new ArgumentException("newCipher no puede estar vacío.", nameof(newCipher));
        }
        ApiTokenCipher = newCipher;
        UpdatedAt = now;
    }

    public void UpdateServices(string? aethraService, string? fallbackService, bool fallbackNoTlsVerify, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(aethraService)) { AethraService = aethraService.Trim(); }
        if (fallbackService is not null) { FallbackService = fallbackService.Trim(); }
        FallbackNoTlsVerify = fallbackNoTlsVerify;
        UpdatedAt = now;
    }

    public void SetTargetVm(string? vmId, DateTimeOffset now)
    {
        TargetVmId = string.IsNullOrWhiteSpace(vmId) ? null : vmId.Trim();
        UpdatedAt = now;
    }

    public void MarkSynced(DateTimeOffset now)
    {
        LastSyncedAt = now;
        UpdatedAt = now;
    }

    // EF Core
    private CloudflareTunnel() : base()
    {
        TunnelId = string.Empty;
        Name = string.Empty;
        AccountId = string.Empty;
        ApiTokenCipher = Array.Empty<byte>();
        AethraService = string.Empty;
        FallbackService = string.Empty;
    }
}
