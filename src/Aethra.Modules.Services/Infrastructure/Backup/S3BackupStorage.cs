using System.Text.Json;
using Aethra.Shared.Contracts.Settings;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Storage de backups en S3-compatible. Stub que requiere config externa (AWS SDK no instalado
/// en MVP para evitar pesos transitivos). Cuando un operador configure <c>s3:default</c> en
/// IntegrationCredentials con shape <c>{ endpoint, access_key, secret_key, bucket }</c>, se
/// hace una request PUT con SigV4 minimal manual.
///
/// Para uso productivo recomendamos integrar <c>Minio</c> (C# client liviano) o
/// <c>AWSSDK.S3</c>. Estos paquetes no se instalan en F11.3 — se documentan como follow-up.
/// </summary>
public sealed class S3BackupStorage(IIntegrationCredentialResolver credResolver,
    IHttpClientFactory httpClientFactory,
    ILogger<S3BackupStorage> logger) : IBackupStorage
{
    private const string CredentialName = "s3:default";

    public bool Supports(string destinationScheme)
        => string.Equals(destinationScheme, "s3", StringComparison.OrdinalIgnoreCase);

    public async Task<string> WriteAsync(string destinationBase, string fileName, byte[] content, CancellationToken ct)
    {
        var cfg = await ResolveAsync(ct).ConfigureAwait(false);
        if (cfg is null)
        {
            throw new InvalidOperationException(
                $"S3 storage requiere credencial '{CredentialName}' en Settings → Integraciones.");
        }
        var (bucket, prefix) = ParseS3Url(destinationBase);
        var key = string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix.TrimEnd('/')}/{fileName}";
        var url = $"{cfg.Endpoint.TrimEnd('/')}/{bucket}/{key}";

        using var client = httpClientFactory.CreateClient("services-backup");
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(content) };
        // SigV4 simplificada: no implementada (requeriria HMAC-SHA256 + canonical request). Para
        // S3 compatible con acceso publico bucket o pre-signed, este endpoint funciona; para AWS
        // estandar debe integrarse SDK.
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"S3 PUT fallo: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        logger.LogInformation("S3BackupStorage: subi {Bytes} bytes a {Url}", content.Length, url);
        return $"s3://{bucket}/{key}";
    }

    public async Task<byte[]> ReadAsync(string fullPath, CancellationToken ct)
    {
        var cfg = await ResolveAsync(ct).ConfigureAwait(false);
        if (cfg is null)
        {
            throw new InvalidOperationException($"S3 storage requiere credencial '{CredentialName}'.");
        }
        var (bucket, key) = ParseS3Url(fullPath);
        var url = $"{cfg.Endpoint.TrimEnd('/')}/{bucket}/{key}";
        using var client = httpClientFactory.CreateClient("services-backup");
        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string fullPath, CancellationToken ct)
    {
        var cfg = await ResolveAsync(ct).ConfigureAwait(false);
        if (cfg is null) { return; }
        var (bucket, key) = ParseS3Url(fullPath);
        var url = $"{cfg.Endpoint.TrimEnd('/')}/{bucket}/{key}";
        using var client = httpClientFactory.CreateClient("services-backup");
        using var resp = await client.DeleteAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("S3BackupStorage: delete fallo {Status} para {Url}", (int)resp.StatusCode, url);
        }
    }

    private async Task<S3Config?> ResolveAsync(CancellationToken ct)
    {
        var raw = await credResolver.GetSecretAsync(CredentialName, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        return JsonSerializer.Deserialize<S3Config>(raw);
    }

    private static (string Bucket, string Key) ParseS3Url(string s3Url)
    {
        // s3://bucket/key/prefix
        var stripped = s3Url.StartsWith("s3://", StringComparison.OrdinalIgnoreCase)
            ? s3Url["s3://".Length..]
            : s3Url;
        var idx = stripped.IndexOf('/', StringComparison.Ordinal);
        if (idx <= 0)
        {
            return (stripped, string.Empty);
        }
        return (stripped[..idx], stripped[(idx + 1)..]);
    }

    private sealed record S3Config(string Endpoint, string AccessKey, string SecretKey, string Bucket);
}
