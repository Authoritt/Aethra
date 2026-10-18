using System.Collections.Concurrent;
using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using UptimeMonitor = Aethra.Modules.Monitoring.Domain.Monitor;

namespace Aethra.Modules.Monitoring.Tests;

public sealed class MonitorUrlBackfillPostgreSqlTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private const string InitialMigration = "20260530055806_Initial";

    [Fact]
    public async Task Create_rejects_a_second_monitor_that_differs_only_by_fragment()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        await db.Database.MigrateAsync();
        var handler = new CreateMonitorHandler(db, new FixedClock(DateTimeOffset.UtcNow));

        var first = await handler.Handle(CreateCommand("primary", "https://example.com/health#Primary"), default);
        var second = await handler.Handle(CreateCommand("lowercase", "https://example.com/health#primary"), default);

        first.IsSuccess.Should().BeTrue();
        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("monitor.url_taken");
    }

    [Fact]
    public async Task Create_rejects_a_second_monitor_with_lowercase_percent_escape_hex_digits()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        await db.Database.MigrateAsync();
        var handler = new CreateMonitorHandler(db, new FixedClock(DateTimeOffset.UtcNow));

        var first = await handler.Handle(CreateCommand("uppercase", "https://example.com/a%2Fb"), default);
        var second = await handler.Handle(CreateCommand("lowercase", "https://example.com/a%2fb"), default);

        first.IsSuccess.Should().BeTrue();
        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("monitor.url_taken");
    }

    [Fact]
    public async Task Startup_backfill_runs_when_migration_is_applied_and_startup_migrations_are_disabled()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);

        var unicodeId = MonitorId.New().ToString();
        var originalId = MonitorId.New().ToString();
        var duplicateId = MonitorId.New().ToString();
        var fragmentDuplicateId = MonitorId.New().ToString();
        var unicodeUrl = "https://İ.EXAMPLE/Health";
        var duplicateUrl = "https://EXAMPLE.com/health/";
        var fragmentDuplicateUrl = $"https://example.com/health#duplicado-{duplicateId}";
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await InsertLegacyMonitorAsync(db, unicodeId, "unicode", unicodeUrl, createdAt);
        await InsertLegacyMonitorAsync(db, originalId, "original", duplicateUrl, createdAt.AddMinutes(1));
        await InsertLegacyMonitorAsync(
            db,
            duplicateId,
            "duplicate",
            " HTTPS://example.com/health ",
            createdAt.AddMinutes(2));
        await InsertLegacyMonitorAsync(
            db,
            fragmentDuplicateId,
            "fragment",
            fragmentDuplicateUrl,
            createdAt.AddMinutes(3));

        // This models the managed-production path: the deployment pipeline applies the
        // migration, while the application does not run MigrateAsync during startup.
        await migrator.MigrateAsync();
        const bool applyMigrationsOnStart = false;
        applyMigrationsOnStart.Should().BeFalse();

        var logger = new CollectingLogger();
        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, logger)).Should().Be(4);

        var rows = await ReadBackfillValuesAsync(db);
        AssertRuntimeKey(rows[unicodeId], unicodeUrl);
        AssertRuntimeKey(rows[originalId], duplicateUrl);
        rows[fragmentDuplicateId].NormalizedUrl.Should().Be(UptimeMonitor.NormalizeUrl(duplicateUrl));
        rows[fragmentDuplicateId].Key.Should().MatchRegex("^dup-[0-9a-f]{60}$");
        rows[duplicateId].NormalizedUrl.Should().Be(UptimeMonitor.NormalizeUrl(duplicateUrl));
        rows[duplicateId].Key.Should().MatchRegex("^dup-[0-9a-f]{60}$");
        rows.Values.Select(row => row.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Backfill_keeps_distinct_trailing_query_values_as_unique_monitors()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);
        var slashId = MonitorId.New().ToString();
        var emptyId = MonitorId.New().ToString();
        const string slashUrl = "https://example.com/check?redirect=/";
        const string emptyUrl = "https://example.com/check?redirect=";
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await InsertLegacyMonitorAsync(db, slashId, "slash-query", slashUrl, createdAt);
        await InsertLegacyMonitorAsync(db, emptyId, "empty-query", emptyUrl, createdAt.AddMinutes(1));
        await migrator.MigrateAsync();
        var logger = new CollectingLogger();

        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, logger)).Should().Be(2);

        var rows = await ReadBackfillValuesAsync(db);
        AssertRuntimeKey(rows[slashId], slashUrl);
        AssertRuntimeKey(rows[emptyId], emptyUrl);
        rows.Values.Select(row => row.Key).Should().OnlyHaveUniqueItems();
        logger.WarningMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Startup_backfill_does_nothing_when_required_migration_is_not_applied()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync(InitialMigration);
        await InsertLegacyMonitorAsync(
            db,
            MonitorId.New().ToString(),
            "legacy",
            "https://example.com/health",
            DateTimeOffset.UtcNow);
        var logger = new CollectingLogger();

        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, logger)).Should().Be(0);

        logger.Messages.Should().ContainSingle(message =>
            message.Contains(MonitorUrlBackfill.RequiredMigration, StringComparison.Ordinal)
            && message.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Concurrent_startup_backfills_are_serialized_and_do_not_duplicate_keys()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        var firstId = MonitorId.New().ToString();
        var secondId = MonitorId.New().ToString();
        await using (var setup = new MonitoringDbContext(options))
        {
            var migrator = setup.GetService<IMigrator>();
            await migrator.MigrateAsync(InitialMigration);
            await InsertLegacyMonitorAsync(
                setup,
                firstId,
                "first",
                "https://example.com/health",
                DateTimeOffset.UtcNow);
            await InsertLegacyMonitorAsync(
                setup,
                secondId,
                "second",
                " HTTPS://EXAMPLE.COM/health/ ",
                DateTimeOffset.UtcNow.AddMinutes(1));
            await migrator.MigrateAsync();
        }

        await using var firstDb = new MonitoringDbContext(options);
        await using var secondDb = new MonitoringDbContext(options);
        var results = await Task.WhenAll(
            MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(firstDb, new CollectingLogger()),
            MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(secondDb, new CollectingLogger()));

        results.Sum().Should().Be(2);
        await using var verificationDb = new MonitoringDbContext(options);
        var rows = await ReadBackfillValuesAsync(verificationDb);
        rows.Should().HaveCount(2);
        rows.Values.Should().OnlyContain(row => !string.IsNullOrEmpty(row.Key));
        rows.Values.Select(row => row.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Duplicate_warning_contains_only_ids_and_not_the_url_or_remediation_key()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);
        var keptId = MonitorId.New().ToString();
        var duplicateId = MonitorId.New().ToString();
        const string sensitiveUrl = "https://user:password@example.com/health?token=secret-token";
        await InsertLegacyMonitorAsync(db, keptId, "kept", sensitiveUrl, DateTimeOffset.UtcNow);
        await InsertLegacyMonitorAsync(db, duplicateId, "duplicate", sensitiveUrl, DateTimeOffset.UtcNow.AddMinutes(1));
        await migrator.MigrateAsync();
        var logger = new CollectingLogger();

        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, logger)).Should().Be(2);

        var warning = logger.WarningMessages.Should().ContainSingle().Subject;
        warning.Should().Contain(keptId).And.Contain(duplicateId);
        warning.Should().NotContain(sensitiveUrl)
            .And.NotContain("user:password")
            .And.NotContain("secret-token")
            .And.NotContain("dup-");
    }

    [Fact]
    public async Task Reconciliation_corrects_a_stale_key_after_an_old_instance_changes_the_url()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        await db.Database.MigrateAsync();
        var id = MonitorId.New().ToString();
        await InsertLegacyMonitorAsync(
            db,
            id,
            "stale",
            "https://old.example.com/health",
            DateTimeOffset.UtcNow);
        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, new CollectingLogger())).Should().Be(1);

        const string newUrl = "https://NEW.example.com/readiness/";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE monitoring.monitors SET url = {newUrl} WHERE id = {id}
            """);

        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, new CollectingLogger())).Should().Be(1);

        var row = (await ReadBackfillValuesAsync(db))[id];
        AssertRuntimeKey(row, newUrl);
    }

    [Fact]
    public async Task Reconciliation_corrects_a_row_created_without_a_key()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        await db.Database.MigrateAsync();
        var id = MonitorId.New().ToString();
        const string url = "https://example.com/created-by-old-instance";
        await InsertLegacyMonitorAsync(db, id, "missing", url, DateTimeOffset.UtcNow);

        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, new CollectingLogger())).Should().Be(1);

        AssertRuntimeKey((await ReadBackfillValuesAsync(db))[id], url);
    }

    [Fact]
    public async Task Second_reconciliation_does_not_write_when_every_key_is_current()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        await db.Database.MigrateAsync();
        var keptId = MonitorId.New().ToString();
        var duplicateId = MonitorId.New().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        await InsertLegacyMonitorAsync(
            db,
            keptId,
            "kept",
            "https://example.com/current",
            createdAt);
        await InsertLegacyMonitorAsync(
            db,
            duplicateId,
            "duplicate",
            " HTTPS://EXAMPLE.COM/current/ ",
            createdAt.AddMinutes(1));
        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, new CollectingLogger())).Should().Be(2);
        var before = await ReadBackfillValuesAsync(db);
        before[duplicateId].Key.Should().StartWith("dup-");

        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, new CollectingLogger())).Should().Be(0);

        var after = await ReadBackfillValuesAsync(db);
        after.Should().Equal(before);
    }

    [Fact]
    public async Task Remediation_key_is_replaced_with_the_real_hash_after_the_duplicate_is_resolved()
    {
        var options = await postgres.CreateDatabaseOptionsAsync();
        await using var db = new MonitoringDbContext(options);
        await db.Database.MigrateAsync();
        var keptId = MonitorId.New().ToString();
        var duplicateId = MonitorId.New().ToString();
        const string duplicateUrl = "https://example.com/duplicate";
        var createdAt = DateTimeOffset.UtcNow;
        await InsertLegacyMonitorAsync(db, keptId, "kept", duplicateUrl, createdAt);
        await InsertLegacyMonitorAsync(db, duplicateId, "duplicate", duplicateUrl, createdAt.AddMinutes(1));
        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, new CollectingLogger())).Should().Be(2);
        (await ReadBackfillValuesAsync(db))[duplicateId].Key.Should().StartWith("dup-");

        const string resolvedUrl = "https://example.com/resolved";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE monitoring.monitors SET url = {resolvedUrl} WHERE id = {duplicateId}
            """);

        (await MonitorUrlBackfill.BackfillIfMigrationAppliedAsync(db, new CollectingLogger())).Should().Be(1);

        AssertRuntimeKey((await ReadBackfillValuesAsync(db))[duplicateId], resolvedUrl);
    }

    private static void AssertRuntimeKey(BackfillRow row, string url)
    {
        var normalized = UptimeMonitor.NormalizeUrl(url);
        row.NormalizedUrl.Should().Be(normalized);
        row.Key.Should().Be(UptimeMonitor.HashNormalizedUrl(normalized));
    }

    private static CreateMonitorCommand CreateCommand(string slug, string url)
        => new(slug, slug, url, "GET", [200], 60, 5000, null, null, null, null);

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
        command.CommandText = """
            SELECT id, normalized_url, normalized_url_hash, xmin::text::bigint
            FROM monitoring.monitors
            ORDER BY id
            """;
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(
                reader.GetString(0),
                new BackfillRow(reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        }
        return result;
    }

    private sealed record BackfillRow(string NormalizedUrl, string Key, long Version);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyCollection<string> Messages => _entries.Select(entry => entry.Message).ToArray();
        public IReadOnlyCollection<string> WarningMessages => _entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .Select(entry => entry.Message)
            .ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<DbContextOptions<MonitoringDbContext>> CreateDatabaseOptionsAsync()
    {
        var databaseName = $"monitoring_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

        return new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), errorCodesToAdd: null))
            .Options;
    }
}
