using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using UptimeMonitor = Aethra.Modules.Monitoring.Domain.Monitor;

namespace Aethra.Modules.Monitoring.Tests;

public sealed class MonitorUrlUniquenessTests
{
    [Theory]
    [InlineData(" https://EXAMPLE.com/health/ ", "https://example.com/health")]
    [InlineData("https://example.com/health", "https://example.com/health")]
    public void NormalizeUrl_matches_create_and_update_semantics(string url, string expected)
        => UptimeMonitor.NormalizeUrl(url).Should().Be(expected);

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
    public void Normalized_url_has_a_unique_database_index_for_concurrent_writes()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql("Host=localhost;Database=aethra_model_test")
            .Options;
        using var db = new MonitoringDbContext(options);

        var entity = db.Model.FindEntityType(typeof(UptimeMonitor));
        var index = entity!.GetIndexes().Single(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(UptimeMonitor.NormalizedUrl));

        index.IsUnique.Should().BeTrue();
        index.GetDatabaseName().Should().Be("ux_monitors_normalized_url");
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
