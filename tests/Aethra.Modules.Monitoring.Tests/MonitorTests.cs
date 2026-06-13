using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Domain.Events;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;
// Desambiguar frente a System.Threading.Monitor (lo trae ImplicitUsings).
using Monitor = Aethra.Modules.Monitoring.Domain.Monitor;

namespace Aethra.Modules.Monitoring.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Monitor"/> (uptime HTTP): validación de URL, clamping de
/// intervalo/timeout, normalización de status codes esperados, el cómputo de
/// <see cref="Monitor.ConsecutiveFailures"/> + emisión de evento solo en transición, y
/// <see cref="Monitor.IsDueAt"/> / Enable / Disable.
/// </summary>
public sealed class MonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Slug Sl => Slug.Create("uptime").Value;

    private static Monitor NewMonitor(int interval = 60, int timeout = 5000, IReadOnlyList<int>? codes = null)
        => Monitor.Create(Sl, "Uptime", "https://app.example.com/health", MonitorHttpMethod.GET,
            codes ?? [200], interval, timeout, null, null, null, null, Now);

    private static MonitorCheck Check(MonitorStatus status, DateTimeOffset? at = null)
        => MonitorCheck.Create(MonitorId.New(), at ?? Now, status, 200, 10, null, null);

    [Fact]
    public void Create_sets_defaults_and_raises_created_event()
    {
        var m = NewMonitor();

        m.IsEnabled.Should().BeTrue();
        m.LastStatus.Should().Be(MonitorStatus.Unknown);
        m.ConsecutiveFailures.Should().Be(0);
        m.Url.Should().Be("https://app.example.com/health");
        m.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<MonitorCreatedEvent>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://host/x")]
    [InlineData("/relative")]
    public void Create_throws_on_invalid_url(string url)
    {
        var act = () => Monitor.Create(Sl, "n", url, MonitorHttpMethod.GET, [200], 60, 5000,
            null, null, null, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1, 30)]        // bajo el mínimo → 30
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(99999, 3600)]  // sobre el máximo → 3600
    public void Create_clamps_interval(int input, int expected)
    {
        NewMonitor(interval: input).IntervalSec.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(5000, 5000)]
    [InlineData(99999, 60000)]
    public void Create_clamps_timeout(int input, int expected)
    {
        NewMonitor(timeout: input).TimeoutMs.Should().Be(expected);
    }

    [Fact]
    public void Create_defaults_expected_codes_to_200_when_empty()
    {
        NewMonitor(codes: []).ExpectedStatusCodes.Should().Equal(200);
    }

    [Fact]
    public void Create_filters_dedups_and_sorts_expected_codes()
    {
        var m = NewMonitor(codes: [500, 200, 200, 99, 600, 301]);

        m.ExpectedStatusCodes.Should().Equal(200, 301, 500); // 99 y 600 fuera de rango, 200 dedup, ordenado
    }

    [Fact]
    public void Create_defaults_to_200_when_all_codes_are_out_of_range()
    {
        NewMonitor(codes: [99, 600, 700]).ExpectedStatusCodes.Should().Equal(200);
    }

    [Fact]
    public void RecordCheck_increments_failures_on_down_and_resets_on_up()
    {
        var m = NewMonitor();

        m.RecordCheck(Check(MonitorStatus.Down));
        m.RecordCheck(Check(MonitorStatus.Down));
        m.ConsecutiveFailures.Should().Be(2);
        m.LastStatus.Should().Be(MonitorStatus.Down);

        m.RecordCheck(Check(MonitorStatus.Up));
        m.ConsecutiveFailures.Should().Be(0);
        m.LastStatus.Should().Be(MonitorStatus.Up);
    }

    [Fact]
    public void RecordCheck_counts_degraded_as_a_failure()
    {
        var m = NewMonitor();

        m.RecordCheck(Check(MonitorStatus.Degraded));

        m.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void RecordCheck_raises_status_changed_event_only_on_transition()
    {
        var m = NewMonitor();
        m.ClearDomainEvents();

        m.RecordCheck(Check(MonitorStatus.Up));   // Unknown → Up: evento
        m.RecordCheck(Check(MonitorStatus.Up));   // Up → Up: sin evento

        m.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<MonitorStatusChangedEvent>();
    }

    [Fact]
    public void IsDueAt_is_true_when_never_checked()
    {
        NewMonitor().IsDueAt(Now).Should().BeTrue();
    }

    [Fact]
    public void IsDueAt_respects_the_interval()
    {
        var m = NewMonitor(interval: 60);
        m.RecordCheck(Check(MonitorStatus.Up, at: Now));

        m.IsDueAt(Now.AddSeconds(59)).Should().BeFalse();
        m.IsDueAt(Now.AddSeconds(60)).Should().BeTrue();
    }

    [Fact]
    public void IsDueAt_is_false_when_disabled()
    {
        var m = NewMonitor();
        m.Disable(Now);

        m.IsDueAt(Now.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void Disable_sets_flag_raises_event_once_then_is_idempotent()
    {
        var m = NewMonitor();
        m.ClearDomainEvents();

        m.Disable(Now);
        m.IsEnabled.Should().BeFalse();
        m.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<MonitorDisabledEvent>();

        m.ClearDomainEvents();
        m.Disable(Now);
        m.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Enable_after_disable_re_enables()
    {
        var m = NewMonitor();
        m.Disable(Now);

        m.Enable(Now);

        m.IsEnabled.Should().BeTrue();
    }
}
