using System.IO.Compression;
using System.Text;
using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Containers;
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
    ISatelliteRpcClient rpc,
    ICentralVmIdProvider centralVm,
    ILogger<PostgresBackupEngine> logger) : IBackupEngine
{
    private const int RemoteDumpTimeoutSeconds = 900;
    private const int RemoteChunkTimeoutSeconds = 60;

    public ServiceType Type => ServiceType.Postgres;

    public async Task<byte[]> CreateBackupAsync(ManagedService service, CancellationToken ct)
    {
        // OT-008 T10: servicio en una VM distinta de la del central -> dump DENTRO del contenedor por la
        // RPC de exec del satelite. En la VM del central (o si no se conoce) el camino es el de siempre.
        if (BackupExecutionRules.RunsOnRemoteSatellite(service.TargetVmId, centralVm.GetCentralVmId()))
        {
            return await CreateRemoteBackupAsync(service, ct).ConfigureAwait(false);
        }

        var admin = codec.Decode(service.AdminCredentialsCipher);
        var host = await hostResolver.ResolveAsync(service, ct).ConfigureAwait(false);
        var connString = BuildConnString(host, service.InternalPort, admin, database: admin.Username);

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Listar tablas de TODOS los schemas user-land (no sólo 'public'): la DB del control-plane y de
        // los módulos es multi-schema (projects, vms, metrics, monitoring, ...). Antes sólo se respaldaba
        // 'public' → dumps casi vacíos para DBs multi-schema. Excluimos los catálogos del sistema.
        var tables = new List<(string Schema, string Table)>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT table_schema, table_name FROM information_schema.tables "
            + "WHERE table_schema NOT IN ('pg_catalog', 'information_schema') "
            + "AND table_type = 'BASE TABLE' ORDER BY table_schema, table_name",
            conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
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

            foreach (var (schema, table) in tables)
            {
                var qualified = $"{PostgresIdentifier.Quote(schema)}.{PostgresIdentifier.Quote(table)}";
                await writer.WriteLineAsync($"COPY {qualified} FROM STDIN;").ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);

                await using var copy = await conn.BeginTextExportAsync(
                    $"COPY {qualified} TO STDOUT", ct).ConfigureAwait(false);
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
        if (BackupExecutionRules.RunsOnRemoteSatellite(service.TargetVmId, centralVm.GetCentralVmId()))
        {
            await RestoreRemoteBackupAsync(service, backupContent, ct).ConfigureAwait(false);
            return;
        }

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

    private async Task<byte[]> CreateRemoteBackupAsync(ManagedService service, CancellationToken ct)
    {
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var tag = Guid.NewGuid().ToString("N");
        var cmd = BackupExecutionRules.BuildDumpCommand(tag, admin.Username, admin.Password, admin.Username);
        var res = await rpc.SendExecAsync(
            service.TargetVmId, service.ContainerName, cmd, RemoteDumpTimeoutSeconds, ct).ConfigureAwait(false);
        var bytes = BackupExecutionRules.DecodeDumpOutput(res.ExitCode, res.TimedOut, res.Stdout, res.Stderr);
        logger.LogInformation(
            "PostgresBackupEngine: dump remoto de {Slug} en VM {Vm}: {Bytes} bytes (gzip)",
            service.Slug, service.TargetVmId, bytes.Length);
        return bytes;
    }

    private async Task RestoreRemoteBackupAsync(ManagedService service, byte[] backup, CancellationToken ct)
    {
        BackupExecutionRules.EnsureRestorableRemote(backup);
        var admin = codec.Decode(service.AdminCredentialsCipher);
        var tag = Guid.NewGuid().ToString("N");
        var b64 = Convert.ToBase64String(backup);
        try
        {
            var first = true;
            foreach (var chunk in BackupExecutionRules.SplitBase64(b64))
            {
                var up = await rpc.SendExecAsync(
                    service.TargetVmId, service.ContainerName,
                    BackupExecutionRules.BuildRestoreAppendCommand(tag, chunk, first),
                    RemoteChunkTimeoutSeconds, ct).ConfigureAwait(false);
                if (up.ExitCode != 0 || up.TimedOut)
                {
                    throw new InvalidOperationException(
                        $"Restore remoto: fallo la subida del backup al contenedor (exit {up.ExitCode}): {up.Stderr}");
                }
                first = false;
            }

            var apply = await rpc.SendExecAsync(
                service.TargetVmId, service.ContainerName,
                BackupExecutionRules.BuildRestoreApplyCommand(tag, admin.Username, admin.Password, admin.Username),
                RemoteDumpTimeoutSeconds, ct).ConfigureAwait(false);
            if (apply.ExitCode != 0 || apply.TimedOut)
            {
                throw new InvalidOperationException(
                    $"Restore remoto: psql fallo (exit {apply.ExitCode}, timeout={apply.TimedOut}): {apply.Stderr}");
            }
        }
        finally
        {
            try
            {
                await rpc.SendExecAsync(
                    service.TargetVmId, service.ContainerName,
                    BackupExecutionRules.BuildRestoreCleanupCommand(tag), RemoteChunkTimeoutSeconds,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PostgresBackupEngine: no se pudo limpiar /tmp del restore remoto de {Slug}", service.Slug);
            }
        }
        logger.LogInformation("PostgresBackupEngine: restore remoto OK para {Slug}", service.Slug);
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
