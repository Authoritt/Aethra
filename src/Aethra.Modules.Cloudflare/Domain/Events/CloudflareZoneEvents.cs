using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Cloudflare.Domain.Events;

/// <summary>
/// Una zona externa ha sido registrada en Aethra (token cifrado + metadata sincronizada).
/// </summary>
public sealed record CloudflareZoneRegisteredEvent(
    CloudflareZoneId ZoneId,
    string ExternalZoneId,
    string Name,
    string AccountId) : DomainEvent;

/// <summary>
/// El token API de la zona ha sido reemplazado. No incluimos el ciphertext en el evento.
/// </summary>
public sealed record CloudflareZoneTokenRotatedEvent(
    CloudflareZoneId ZoneId,
    string ExternalZoneId) : DomainEvent;
