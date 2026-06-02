using System.IO.Compression;
using System.Text;
using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Engine de backup para Postgres. En lugar de invocar <c>pg_dumpall</c> via docker exec (lo
/// cual requeriria un canal de exec en el satellite RPC que no existe en MVP), genera el dump
/// directamente: lista tablas via INFORMATION_SCHEMA y usa COPY TO STDOUT para extraer datos.
///
/// El output es un archivo gzip con formato <c>aethra-pg-dump v1</c>:
/// <code>
/// -- header
/// COPY public.&lt;table&gt; FROM STDIN;
/// &lt;tab-separated rows&gt;
/// \.
/// </code>
/// El restore re-aplica con <c>COPY FROM STDIN</c>.
/// </summary>
public sealed class PostgresBackupEngine(
    IAdminCredentialsCodec codec,
    IManagedServiceHostResolver hostResolver,
    ILogger<PostgresBackupEngine> logger) : IBackupEngine
{
    public ServiceType Type => ServiceType.Postgres;

    public async Task<byte[]> CreateBackupAsync(ManagedService service, CancellationToken ct)
    {
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var host = await hostResolver.ResolveAsync(service, ct).ConfigureAwait(false);
        var connString = BuildConnString(host, service.InternalPort, admin, database: admin.Username);

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Listar tablas user-land del schema public.
        var tables = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables "
            + "WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name",
            conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        await using var ms = new MemoryStream();
        await using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        await using (var writer = new StreamWriter(gz, new UTF8Encoding(false)))
        {
            await writer.WriteLineAsync("-- aethra-pg-dump v1").ConfigureAwait(false);
            await writer.WriteLineAsync($"-- service: {service.Slug}").ConfigureAwait(false);
            await writer.WriteLineAsync($"-- timestamp: {DateTimeOffset.UtcNow:O}").ConfigureAwait(false);
            await writer.WriteLineAsync($"-- tables: {tables.Count}").ConfigureAwait(false);

            foreach (var table in tables)
            {
                var quoted = PostgresIdentifier.Quote(table);
                await writer.WriteLineAsync($"COPY public.{quoted} FROM STDIN;").ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);

                await using var copy = await conn.BeginTextExportAsync(
                    $"COPY public.{quoted} TO STDOUT", ct).ConfigureAwait(false);
                var buffer = new char[8192];
                int read;
                while ((read = await copy.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await writer.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                await writer.WriteLineAsync(@"\.").ConfigureAwait(false);
            }
        }

        var bytes = ms.ToArray();
        logger.LogInformation(
            "PostgresBackupEngine: dumped {Tables} tablas en {Bytes} bytes para {Slug}",
            tables.Count, bytes.Length, service.Slug);
        return bytes;
    }

    public async Task RestoreBackupAsync(ManagedService service, byte[] backupContent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(backupContent);
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var host = await hostResolver.ResolveAsync(service, ct).ConfigureAwait(false);
        var connString = BuildConnString(host, service.InternalPort, admin, database: admin.Username);

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var ms = new MemoryStream(backupContent);
        await using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, new UTF8Encoding(false));

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (line.StartsWith("--", StringComparison.Ordinal)) { continue; }
            if (!line.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase)) { continue; }

            // Parseamos "COPY <table> FROM STDIN;" y empezamos COPY in.
            var copyCmd = line.TrimEnd(';');
            await using var copyIn = await conn.BeginTextImportAsync(copyCmd, ct).ConfigureAwait(false);

            string? row;
            while ((row = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                if (row == @"\.") { break; }
                await copyIn.WriteAsync(row.AsMemory(), ct).ConfigureAwait(false);
                await copyIn.WriteAsync('\n').ConfigureAwait(false);
            }
        }
        logger.LogInformation("PostgresBackupEngine: restore OK para {Slug}", service.Slug);
    }

    private static string BuildConnString(string host, int port, AdminCredentials admin, string database)
    {
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Username = admin.Username,
            Password = admin.Password,
            Database = database,
            Timeout = 30,
            CommandTimeout = 600,
        };
        return b.ToString();
    }
}
