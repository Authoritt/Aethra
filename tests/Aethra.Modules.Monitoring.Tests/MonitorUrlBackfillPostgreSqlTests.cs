using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;
using UptimeMonitor = Aethra.Modules.Monitoring.Domain.Monitor;

namespace Aethra.Modules.Monitoring.Tests;

public sealed class MonitorUrlBackfillPostgreSqlTests
{
    private const string InitialMigration = "20260530055806_Initial";

    [Fact]
    public async Task Migration_and_runtime_backfill_handle_unicode_legitimate_fragments_duplicates_and_retries()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:18")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MonitoringDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);

        var unicodeId = MonitorId.New().ToString();
        var originalId = MonitorId.New().ToString();
        var duplicateId = MonitorId.New().ToString();
        var legitimateFragmentId = MonitorId.New().ToString();
        var unicodeUrl = "https://İ.EXAMPLE/Health";
        var duplicateUrl = "https://EXAMPLE.com/health/";
        var legitimateFragmentUrl = $"https://example.com/health#duplicado-{duplicateId}";
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await InsertLegacyMonitorAsync(db, unicodeId, "unicode", unicodeUrl, createdAt);
        await InsertLegacyMonitorAsync(db, originalId, "original", duplicateUrl, createdAt.AddMinutes(1));
        await InsertLegacyMonitorAsync(db, duplicateId, "duplicate", " HTTPS://example.com/health ", createdAt.AddMinutes(2));
        await InsertLegacyMonitorAsync(db, legitimateFragmentId, "fragment", legitimateFragmentUrl, createdAt.AddMinutes(3));

        await migrator.MigrateAsync();

        (await ScalarAsync<long>(db, "SELECT count(*) FROM monitoring.monitors WHERE normalized_url_hash IS NULL"))
            .Should().Be(4);
        (await ScalarAsync<string>(db,
                "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'monitoring' AND table_name = 'monitors' AND column_name = 'normalized_url_hash'"))
            .Should().Be("YES");
        (await ScalarAsync<string>(db,
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'monitoring' AND tablename = 'monitors' AND indexname = 'ux_monitors_normalized_url'"))
            .Should().Contain("UNIQUE");

        var logger = new CollectingLogger();
        (await MonitorUrlBackfill.BackfillAsync(db, logger)).Should().Be(4);
        var firstPass = await ReadBackfillValuesAsync(db);

        AssertRuntimeKey(firstPass[unicodeId], unicodeUrl);
        AssertRuntimeKey(firstPass[originalId], duplicateUrl);
        AssertRuntimeKey(firstPass[legitimateFragmentId], legitimateFragmentUrl);
        firstPass[duplicateId].NormalizedUrl.Should().Be(UptimeMonitor.NormalizeUrl(duplicateUrl));
        firstPass[duplicateId].Key.Should().MatchRegex("^dup-[0-9a-f]{60}$");
        firstPass.Values.Select(row => row.Key).Should().OnlyHaveUniqueItems();
        logger.WarningCount.Should().Be(1);

        (await MonitorUrlBackfill.BackfillAsync(db, logger)).Should().Be(0);
        (await ReadBackfillValuesAsync(db)).Should().BeEquivalentTo(firstPass);
        logger.WarningCount.Should().Be(1);
    }

    private static void AssertRuntimeKey(BackfillRow row, string url)
    {
        var normalized = UptimeMonitor.NormalizeUrl(url);
        row.NormalizedUrl.Should().Be(normalized);
        row.Key.Should().Be(UptimeMonitor.HashNormalizedUrl(normalized));
    }

    private static async Task InsertLegacyMonitorAsync(
        MonitoringDbContext db,
        string id,
        string slug,
        string url,
        DateTimeOffset createdAt)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO monitoring.monitors
                (id, slug, name, url, http_method, interval_sec, timeout_ms, is_enabled,
                 created_at, updated_at, last_status, consecutive_failures, expected_status_codes)
            VALUES
                ({id}, {slug}, {slug}, {url}, 'GET', 60, 5000, true,
                 {createdAt}, {createdAt}, 'Unknown', 0, '[200]'::jsonb)
            """);
    }

    private static async Task<Dictionary<string, BackfillRow>> ReadBackfillValuesAsync(MonitoringDbContext db)
    {
        var result = new Dictionary<string, BackfillRow>(StringComparer.Ordinal);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT id, normalized_url, normalized_url_hash FROM monitoring.monitors ORDER BY id";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0), new BackfillRow(reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }

    private static async Task<T> ScalarAsync<T>(MonitoringDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed record BackfillRow(string NormalizedUrl, string Key);

    private sealed class CollectingLogger : ILogger
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
