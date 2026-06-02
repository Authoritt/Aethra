using System.Net.Http.Headers;
using System.Text;
using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Engine de backup para RabbitMQ. Hace HTTP GET a <c>/api/definitions</c> del Management API
/// (puerto 15672 por defecto) y persiste el JSON resultante. Restore via POST al mismo endpoint.
/// </summary>
public sealed class RabbitMqBackupEngine(
    IAdminCredentialsCodec codec,
    IManagedServiceHostResolver hostResolver,
    IHttpClientFactory httpClientFactory,
    ILogger<RabbitMqBackupEngine> logger) : IBackupEngine
{
    public ServiceType Type => ServiceType.RabbitMQ;

    public async Task<byte[]> CreateBackupAsync(ManagedService service, CancellationToken ct)
    {
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var host = await hostResolver.ResolveAsync(service, ct).ConfigureAwait(false);
        var mgmtPort = await hostResolver.ResolveManagementPortAsync(service, ct).ConfigureAwait(false);
        var url = $"http://{host}:{mgmtPort}/api/definitions";

        using var client = httpClientFactory.CreateClient("services-backup");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{admin.Username}:{admin.Password}")));

        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        logger.LogInformation("RabbitMqBackupEngine: dumped {Bytes} bytes para {Slug}", bytes.Length, service.Slug);
        return bytes;
    }

    public async Task RestoreBackupAsync(ManagedService service, byte[] backupContent, CancellationToken ct)
    {
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var host = await hostResolver.ResolveAsync(service, ct).ConfigureAwait(false);
        var mgmtPort = await hostResolver.ResolveManagementPortAsync(service, ct).ConfigureAwait(false);
        var url = $"http://{host}:{mgmtPort}/api/definitions";

        using var client = httpClientFactory.CreateClient("services-backup");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{admin.Username}:{admin.Password}")));

        using var content = new ByteArrayContent(backupContent);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var resp = await client.PostAsync(url, content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        logger.LogInformation("RabbitMqBackupEngine: restored para {Slug}", service.Slug);
    }
}
