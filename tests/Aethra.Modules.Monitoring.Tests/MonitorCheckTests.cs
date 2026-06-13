using Aethra.Modules.Monitoring.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Monitoring.Tests;

/// <summary>
/// Invariantes de <see cref="MonitorCheck"/> (muestra append-only del probe): rechaza status
/// Unknown, trunca snippet y mensaje de error, y clampa latencias negativas a 0.
/// </summary>
public sealed class MonitorCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_rejects_unknown_status()
    {
        var act = () => MonitorCheck.Create(MonitorId.New(), Now, MonitorStatus.Unknown, 200, 10, null, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_truncates_response_snippet_to_max_length()
    {
        var check = MonitorCheck.Create(MonitorId.New(), Now, MonitorStatus.Down, 500, 10, null,
            new string('x', 500));

        check.ResponseSnippet.Should().NotBeNull();
        check.ResponseSnippet!.Length.Should().Be(MonitorCheck.SnippetMaxLength);
    }

    [Fact]
    public void Create_truncates_error_message_to_1000_chars()
    {
        var check = MonitorCheck.Create(MonitorId.New(), Now, MonitorStatus.Down, null, null,
            new string('e', 2000), null);

        check.ErrorMessage.Should().NotBeNull();
        check.ErrorMessage!.Length.Should().Be(1000);
    }

    [Fact]
    public void Create_clamps_negative_latency_to_zero()
    {
        var check = MonitorCheck.Create(MonitorId.New(), Now, MonitorStatus.Up, 200, -5, null, null);

        check.LatencyMs.Should().Be(0);
    }

    [Fact]
    public void Create_preserves_valid_values()
    {
        var check = MonitorCheck.Create(MonitorId.New(), Now, MonitorStatus.Up, 200, 42, null, "ok");

        check.Status.Should().Be(MonitorStatus.Up);
        check.HttpStatusCode.Should().Be(200);
        check.LatencyMs.Should().Be(42);
        check.ResponseSnippet.Should().Be("ok");
    }
}
