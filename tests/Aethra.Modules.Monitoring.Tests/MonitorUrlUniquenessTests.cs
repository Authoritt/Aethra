using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;
using UptimeMonitor = Aethra.Modules.Monitoring.Domain.Monitor;

namespace Aethra.Modules.Monitoring.Tests;

public sealed class MonitorUrlUniquenessTests
{
    [Theory]
    [InlineData(" https://EXAMPLE.com/health/ ", "https://example.com/health")]
    [InlineData("https://example.com/health", "https://example.com/health")]
    [InlineData("HTTPS://User:Secret@EXAMPLE.COM:8443/Health/Ready?Token=AbC", "https://User:Secret@example.com:8443/Health/Ready?Token=AbC")]
    public void NormalizeUrl_matches_create_and_update_semantics(string url, string expected)
        => UptimeMonitor.NormalizeUrl(url).Should().Be(expected);

    [Fact]
    public void NormalizeUrl_preserves_case_sensitive_path_and_query()
    {
        UptimeMonitor.NormalizeUrl("https://EXAMPLE.com/Health?Token=AbC")
            .Should().NotBe(UptimeMonitor.NormalizeUrl("https://example.com/health?token=abc"));
    }

    [Fact]
    public void Updating_to_current_normalized_url_keeps_the_same_key()
    {
        var monitor = CreateMonitor("https://example.com/health/");

        monitor.UpdateConfig(
            name: null,
            url: " HTTPS://EXAMPLE.COM/health ",
            method: null,
            expectedStatusCodes: null,
            intervalSec: null,
            timeoutMs: null,
            headers: null,
            clearHeaders: false,
            bodyTemplate: null,
            clearBodyTemplate: false,
            instanceId: null,
            clearInstanceId: false,
            projectId: null,
            clearProjectId: false,
            now: DateTimeOffset.UtcNow);

        monitor.NormalizedUrl.Should().Be("https://example.com/health");
    }

    [Fact]
    public void Update_excludes_itself_but_rejects_another_monitors_normalized_url()
    {
        var current = CreateMonitor("https://example.com/current/");
        var other = CreateMonitor("https://example.com/other/");
        var currentUrlConflict = MonitorUrlUniqueness
            .BuildConflictPredicate(" HTTPS://EXAMPLE.COM/current ", current.Id)
            .Compile();
        var otherUrlConflict = MonitorUrlUniqueness
            .BuildConflictPredicate(" HTTPS://EXAMPLE.COM/other ", current.Id)
            .Compile();

        currentUrlConflict(current).Should().BeFalse();
        otherUrlConflict(other).Should().BeTrue();
    }

    [Fact]
    public void Normalized_url_hash_has_a_fixed_size_unique_database_index_for_concurrent_writes()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql("Host=localhost;Database=aethra_model_test")
            .Options;
        using var db = new MonitoringDbContext(options);

        var entity = db.Model.FindEntityType(typeof(UptimeMonitor));
        var index = entity!.GetIndexes().Single(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(UptimeMonitor.NormalizedUrlHash));

        index.IsUnique.Should().BeTrue();
        index.GetDatabaseName().Should().Be("ux_monitors_normalized_url");
        entity.FindProperty(nameof(UptimeMonitor.NormalizedUrlHash))!.GetMaxLength().Should().Be(64);
    }

    [Fact]
    public void Normalized_url_hash_is_fixed_size_even_for_a_long_multibyte_url()
    {
        var normalizedUrl = UptimeMonitor.NormalizeUrl($"https://example.com/{new string('ñ', 2000)}");

        var hash = UptimeMonitor.HashNormalizedUrl(normalizedUrl);

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Hash_unique_index_violation_is_still_translated_to_url_taken_conflict()
    {
        var postgresException = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: MonitorUrlUniqueness.ConstraintName);
        var exception = new DbUpdateException("Could not save monitor.", postgresException);

        MonitorUrlUniqueness.IsUniqueConstraintViolation(exception).Should().BeTrue();
        MonitorUrlUniqueness.Conflict("https://example.com").Code.Should().Be("monitor.url_taken");
    }

    private static UptimeMonitor CreateMonitor(string url)
        => UptimeMonitor.Create(
            Slug.Create("health").Value,
            "Health",
            url,
            MonitorHttpMethod.GET,
            [200],
            60,
            5000,
            headers: null,
            bodyTemplate: null,
            instanceId: null,
            projectId: null,
            DateTimeOffset.UtcNow);
}
