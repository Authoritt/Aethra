using System.Runtime.InteropServices;
using System.Text.Json;
using Aethra.Shared.Contracts.Vms;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Satellite.Buffer;

/// <summary>
/// Implementación SQLite del <see cref="ISnapshotBuffer"/>.
/// El payload se serializa a JSON; el ID autoincremental define el orden cronológico
/// (asumimos que los Enqueue ocurren monotónicamente desde el worker, lo cual es cierto
/// porque solo el <c>SatelliteConnectionWorker</c> escribe).
///
/// Concurrencia: SQLite con Microsoft.Data.Sqlite soporta múltiples readers pero un único
/// writer. Serializamos TODAS las operaciones con un <see cref="SemaphoreSlim"/> para
/// simplicidad y porque el worker no es de alto throughput (1 muestra cada N segundos).
/// </summary>
public sealed class SqliteSnapshotBuffer : ISnapshotBuffer, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;
    private readonly ILogger<SqliteSnapshotBuffer> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _initialized;

    public SqliteSnapshotBuffer(IOptions<SatelliteOptions> options, ILogger<SqliteSnapshotBuffer> logger)
    {
        _logger = logger;

        var path = ResolveBufferPath(options.Value.BufferPath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _logger.LogInformation("Buffer de snapshots persistente en {Path}", path);
    }

    /// <summary>Resuelve el path del buffer: opción explícita > env var > default por OS.</summary>
    private static string ResolveBufferPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var fromEnv = Environment.GetEnvironmentVariable("AETHRA_SATELLITE_BUFFER_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "aethra", "buffer.db");
        }

        return "/var/lib/aethra/buffer.db";
    }

    private async Task EnsureInitializedAsync(SqliteConnection conn, CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS metrics_buffer (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                occurred_at TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_metrics_buffer_occurred_at ON metrics_buffer(occurred_at);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
        _initialized = true;
    }

    public async Task EnqueueAsync(VmMetricSnapshot snapshot, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureInitializedAsync(conn, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO metrics_buffer (occurred_at, payload) VALUES (@ts, @payload);";
            cmd.Parameters.AddWithValue("@ts", snapshot.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(snapshot, JsonOpts));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<BufferedSnapshot>> DrainBatchAsync(int batchSize, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureInitializedAsync(conn, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, payload FROM metrics_buffer ORDER BY id ASC LIMIT @batchSize;";
            cmd.Parameters.AddWithValue("@batchSize", batchSize);

            var results = new List<BufferedSnapshot>(batchSize);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt64(0);
                var payload = reader.GetString(1);
                var snapshot = JsonSerializer.Deserialize<VmMetricSnapshot>(payload, JsonOpts);
                if (snapshot is null)
                {
                    _logger.LogWarning("Payload corrupto en buffer id={Id}; será purgado", id);
                    continue;
                }
                results.Add(new BufferedSnapshot(id, snapshot));
            }
            return results;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task MarkSentAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureInitializedAsync(conn, ct);

            // Construimos IN (@p0,@p1,...) parametrizado para evitar inyección y
            // beneficiarnos del plan de query cacheado.
            await using var cmd = conn.CreateCommand();
            var names = new List<string>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                var name = $"@p{i}";
                names.Add(name);
                cmd.Parameters.AddWithValue(name, ids[i]);
            }
            cmd.CommandText = $"DELETE FROM metrics_buffer WHERE id IN ({string.Join(",", names)});";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await EnsureInitializedAsync(conn, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM metrics_buffer WHERE occurred_at < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            if (deleted > 0)
            {
                _logger.LogInformation("Prune del buffer: {Deleted} muestras eliminadas (cutoff={Cutoff:O})",
                    deleted, cutoff);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();
}
