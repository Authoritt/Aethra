namespace Aethra.Modules.Proxy.UseCases.Dtos;

/// <summary>
/// Vista de lectura de un certificado TLS gestionado: SOLO metadata/estado. NUNCA incluye el PEM ni la
/// clave privada (esos viven cifrados y no se exponen por la API/MCP).
/// </summary>
public sealed record CertificateDto(
    string Id,
    string Hostname,
    string Status,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? NotBefore,
    DateTimeOffset? NotAfter,
    DateTimeOffset? RenewAfter,
    string? LastError);
