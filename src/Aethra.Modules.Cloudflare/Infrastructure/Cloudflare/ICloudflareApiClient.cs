namespace Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;

/// <summary>
/// Abstraccion del API v4 de Cloudflare (<c>https://api.cloudflare.com/client/v4/</c>).
/// Cada metodo recibe el token API en plano (decifrado por el caller) y autentica con
/// <c>Authorization: Bearer {token}</c>. Implementacion real: <c>HttpCloudflareApiClient</c>.
/// </summary>
public interface ICloudflareApiClient
{
    Task<CloudflareZoneInfo> GetZoneAsync(string zoneId, string apiToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<CloudflareDnsRecordInfo>> ListDnsRecordsAsync(
        string zoneId,
        string apiToken,
        CancellationToken cancellationToken);

    Task<string> CreateDnsRecordAsync(
        string zoneId,
        string apiToken,
        CreateDnsRecordRequest request,
        CancellationToken cancellationToken);

    Task UpdateDnsRecordAsync(
        string zoneId,
        string externalRecordId,
        string apiToken,
        UpdateDnsRecordRequest request,
        CancellationToken cancellationToken);

    Task DeleteDnsRecordAsync(
        string zoneId,
        string externalRecordId,
        string apiToken,
        CancellationToken cancellationToken);
}

public sealed record CloudflareZoneInfo(string Id, string Name, string Status, string AccountId);

public sealed record CloudflareDnsRecordInfo(
    string Id,
    string Type,
    string Name,
    string Content,
    int Ttl,
    bool Proxied,
    string? Comment);

public sealed record CreateDnsRecordRequest(
    string Type,
    string Name,
    string Content,
    int Ttl,
    bool Proxied,
    string? Comment);

public sealed record UpdateDnsRecordRequest(
    string Type,
    string Name,
    string Content,
    int Ttl,
    bool Proxied,
    string? Comment);
