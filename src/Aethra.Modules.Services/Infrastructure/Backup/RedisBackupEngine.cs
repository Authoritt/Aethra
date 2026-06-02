using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Engine de backup para Redis. Estrategia logica (no usa SAVE + copy dump.rdb porque eso
/// requeriria acceso al filesystem del contenedor): enumera keys via SCAN y persiste un JSON
/// gzip con shape <c>[{ key, type, value, ttl }]</c>.
///
/// Apto para datasets pequenos a medianos. Para datasets enormes la estrategia recomendada
/// es BGSAVE + rsync del dump.rdb del volumen — fuera de scope del MVP.
/// </summary>
public sealed class RedisBackupEngine(
    IAdminCredentialsCodec codec,
    IManagedServiceHostResolver hostResolver,
    ILogger<RedisBackupEngine> logger) : IBackupEngine
{
    public ServiceType Type => ServiceType.Redis;

    public async Task<byte[]> CreateBackupAsync(ManagedService service, CancellationToken ct)
    {
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var host = await hostResolver.ResolveAsync(service, ct).ConfigureAwait(false);
        var conf = new ConfigurationOptions
        {
            EndPoints = { { host, service.InternalPort } },
            Password = admin.Password,
            ConnectTimeout = 5_000,
            AbortOnConnectFail = false,
        };

        await using var mux = await ConnectionMultiplexer.ConnectAsync(conf).ConfigureAwait(false);
        var db = mux.GetDatabase();
        var server = mux.GetServer(host, service.InternalPort);

        await using var ms = new MemoryStream();
        await using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        await using (var writer = new Utf8JsonWriter(gz))
        {
            writer.WriteStartObject();
            writer.WriteString("version", "aethra-redis-dump v1");
            writer.WriteString("service", service.Slug);
            writer.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteStartArray("entries");

            var count = 0;
            await foreach (var key in server.KeysAsync(pageSize: 500).WithCancellation(ct).ConfigureAwait(false))
            {
                var type = await db.KeyTypeAsync(key).ConfigureAwait(false);
                var ttl = await db.KeyTimeToLiveAsync(key).ConfigureAwait(false);

                writer.WriteStartObject();
                writer.WriteString("key", (string)key!);
                writer.WriteString("type", type.ToString());
                writer.WriteNumber("ttl_ms", ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : -1);

                switch (type)
                {
                    case RedisType.String:
                        writer.WriteString("value", ((string?)await db.StringGetAsync(key).ConfigureAwait(false)) ?? string.Empty);
                        break;
                    case RedisType.List:
                        writer.WriteStartArray("value");
                        foreach (var item in await db.ListRangeAsync(key).ConfigureAwait(false))
                        {
                            writer.WriteStringValue((string)item!);
                        }
                        writer.WriteEndArray();
                        break;
                    case RedisType.Set:
                        writer.WriteStartArray("value");
                        foreach (var item in await db.SetMembersAsync(key).ConfigureAwait(false))
                        {
                            writer.WriteStringValue((string)item!);
                        }
                        writer.WriteEndArray();
                        break;
                    case RedisType.Hash:
                        writer.WriteStartObject("value");
                        foreach (var entry in await db.HashGetAllAsync(key).ConfigureAwait(false))
                        {
                            writer.WriteString((string)entry.Name!, (string)entry.Value!);
                        }
                        writer.WriteEndObject();
                        break;
                    default:
                        writer.WriteNull("value");
                        break;
                }
                writer.WriteEndObject();
                count++;
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var bytes = ms.ToArray();
        logger.LogInformation("RedisBackupEngine: dumped {Bytes} bytes para {Slug}", bytes.Length, service.Slug);
        return bytes;
    }

    public async Task RestoreBackupAsync(ManagedService service, byte[] backupContent, CancellationToken ct)
    {
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var host = await hostResolver.ResolveAsync(service, ct).ConfigureAwait(false);
        var conf = new ConfigurationOptions
        {
            EndPoints = { { host, service.InternalPort } },
            Password = admin.Password,
            ConnectTimeout = 5_000,
            AbortOnConnectFail = false,
        };
        await using var mux = await ConnectionMultiplexer.ConnectAsync(conf).ConfigureAwait(false);
        var db = mux.GetDatabase();

        using var ms = new MemoryStream(backupContent);
        await using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var doc = await JsonDocument.ParseAsync(gz, cancellationToken: ct).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("entries", out var entries))
        {
            throw new InvalidOperationException("Backup invalido: falta 'entries'.");
        }

        var restored = 0;
        foreach (var e in entries.EnumerateArray())
        {
            var key = e.GetProperty("key").GetString() ?? string.Empty;
            var type = e.GetProperty("type").GetString() ?? string.Empty;
            var ttlMs = e.TryGetProperty("ttl_ms", out var ttlProp) ? ttlProp.GetInt64() : -1;
            var value = e.GetProperty("value");

            switch (type)
            {
                case "String":
                    await db.StringSetAsync(key, value.GetString()).ConfigureAwait(false);
                    break;
                case "List":
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
                    var listItems = value.EnumerateArray()
                        .Select(x => (RedisValue)x.GetString())
                        .ToArray();
                    if (listItems.Length > 0)
                    {
                        await db.ListRightPushAsync(key, listItems).ConfigureAwait(false);
                    }
                    break;
                case "Set":
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
                    var setItems = value.EnumerateArray()
                        .Select(x => (RedisValue)x.GetString())
                        .ToArray();
                    if (setItems.Length > 0)
                    {
                        await db.SetAddAsync(key, setItems).ConfigureAwait(false);
                    }
                    break;
                case "Hash":
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
                    var hashEntries = value.EnumerateObject()
                        .Select(p => new HashEntry(p.Name, p.Value.GetString()))
                        .ToArray();
                    if (hashEntries.Length > 0)
                    {
                        await db.HashSetAsync(key, hashEntries).ConfigureAwait(false);
                    }
                    break;
            }
            if (ttlMs > 0)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromMilliseconds(ttlMs)).ConfigureAwait(false);
            }
            restored++;
        }
        logger.LogInformation("RedisBackupEngine: restored {Count} keys para {Slug}", restored, service.Slug);
    }
}
