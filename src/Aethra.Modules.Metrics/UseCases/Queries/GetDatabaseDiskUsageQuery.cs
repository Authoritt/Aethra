using System.Data;
using System.Globalization;
using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Metrics.UseCases.Queries;

/// <summary>
/// Resumen de uso de disco de la base de datos central (todas las tablas de todos los schemas del
/// monolito). Útil para localizar fugas de disco y validar que la retención (5 BackgroundServices)
/// mantiene acotadas las tablas de alto volumen. Lee del catálogo de Postgres (sin datos de negocio):
/// <c>pg_database_size</c> + <c>pg_total_relation_size</c> (tabla + índices + TOAST) por tabla, con el
/// conteo de filas estimado (<c>n_live_tup</c>, lo actualiza autovacuum/ANALYZE).
/// </summary>
public sealed record GetDatabaseDiskUsageQuery(int TopN = 30) : IQuery<DatabaseDiskUsageDto>;

/// <summary>Vista agregada del uso de disco de la DB. <paramref name="Tables"/> está ordenada desc por bytes.</summary>
public sealed record DatabaseDiskUsageDto(
    long DatabaseSizeBytes,
    long TablesTotalBytes,
    int TableCount,
    IReadOnlyList<TableDiskUsageDto> Tables);

/// <summary>Uso de disco de una tabla: bytes totales (heap+índices+TOAST) y filas estimadas.</summary>
public sealed record TableDiskUsageDto(string Schema, string Table, long TotalBytes, long EstimatedRows);

internal sealed class GetDatabaseDiskUsageHandler(MetricsDbContext db)
    : IQueryHandler<GetDatabaseDiskUsageQuery, DatabaseDiskUsageDto>
{
    public async Task<Result<DatabaseDiskUsageDto>> Handle(GetDatabaseDiskUsageQuery request, CancellationToken ct)
    {
        var topN = Math.Clamp(request.TopN, 1, 200);

        // Reusa la conexión del DbContext (mismo pool). La abrimos sólo si está cerrada y la cerramos
        // al terminar para no dejar la conexión scoped en un estado inesperado.
        var conn = db.Database.GetDbConnection();
        var mustClose = conn.State != ConnectionState.Open;
        if (mustClose)
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
        }

        try
        {
            long dbSize;
            using (var sizeCmd = conn.CreateCommand())
            {
                sizeCmd.CommandText = "SELECT pg_database_size(current_database())";
                var scalar = await sizeCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                dbSize = scalar is null || scalar is DBNull ? 0 : Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
            }

            var tables = new List<TableDiskUsageDto>(topN);
            long tablesTotal = 0;
            var tableCount = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT schemaname, relname, pg_total_relation_size(relid) AS total_bytes, n_live_tup " +
                    "FROM pg_stat_user_tables ORDER BY pg_total_relation_size(relid) DESC";
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var totalBytes = reader.GetInt64(2);
                    tablesTotal += totalBytes;
                    tableCount++;
                    if (tables.Count < topN)
                    {
                        tables.Add(new TableDiskUsageDto(
                            reader.GetString(0),
                            reader.GetString(1),
                            totalBytes,
                            reader.GetInt64(3)));
                    }
                }
            }

            return Result.Success(new DatabaseDiskUsageDto(dbSize, tablesTotal, tableCount, tables));
        }
        finally
        {
            if (mustClose)
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
