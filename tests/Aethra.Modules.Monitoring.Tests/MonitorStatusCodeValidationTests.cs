using Aethra.Modules.Monitoring.UseCases.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Monitoring.Tests;

/// <summary>
/// ExpectedStatusCodes debe estar en 100–599 tanto en create como en update; antes los códigos
/// fuera de rango se descartaban en silencio (Monitor.NormalizeExpected). Ahora se rechazan.
/// </summary>
public sealed class MonitorStatusCodeValidationTests
{
    private static readonly int[] ValidCodes = [200, 204, 301];
    private static readonly int[] OutOfRangeCodes = [2000];
    private static readonly int[] BelowRangeCodes = [99];

    private static CreateMonitorCommand Create(int[] codes)
        => new("uptime", "Uptime", "https://app.example.com/health", "GET", codes, null, null, null, null, null, null);

    private static UpdateMonitorCommand Update(int[] codes)
        => new("mon_ABC", null, null, null, codes, null, null, null, false, null, false, null, false, null, false);

    [Fact]
    public void CreateMonitor_accepts_valid_status_codes()
        => new CreateMonitorValidator().Validate(Create(ValidCodes)).IsValid.Should().BeTrue();

    [Fact]
    public void CreateMonitor_rejects_code_above_599()
        => new CreateMonitorValidator().Validate(Create(OutOfRangeCodes)).IsValid.Should().BeFalse();

    [Fact]
    public void CreateMonitor_rejects_code_below_100()
        => new CreateMonitorValidator().Validate(Create(BelowRangeCodes)).IsValid.Should().BeFalse();

    [Fact]
    public void UpdateMonitor_accepts_valid_status_codes()
        => new UpdateMonitorValidator().Validate(Update(ValidCodes)).IsValid.Should().BeTrue();

    [Fact]
    public void UpdateMonitor_rejects_code_out_of_range()
        => new UpdateMonitorValidator().Validate(Update(OutOfRangeCodes)).IsValid.Should().BeFalse();

    private static CreateMonitorCommand CreateIv(int? interval = null, int? timeout = null)
        => new("uptime", "Uptime", "https://app.example.com/health", "GET", null, interval, timeout, null, null, null, null);

    [Fact]
    public void CreateMonitor_accepts_valid_interval_and_timeout()
        => new CreateMonitorValidator().Validate(CreateIv(interval: 60, timeout: 5000)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(5)]       // < 30
    [InlineData(99999)]   // > 3600
    public void CreateMonitor_rejects_interval_out_of_range(int interval)
        => new CreateMonitorValidator().Validate(CreateIv(interval: interval)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(500)]      // < 1000
    [InlineData(120000)]   // > 60000
    public void CreateMonitor_rejects_timeout_out_of_range(int timeout)
        => new CreateMonitorValidator().Validate(CreateIv(timeout: timeout)).IsValid.Should().BeFalse();

    [Fact]
    public void UpdateMonitor_rejects_interval_out_of_range()
        => new UpdateMonitorValidator().Validate(
            new UpdateMonitorCommand("mon_ABC", null, null, null, null, 5, null, null, false, null, false, null, false, null, false))
            .IsValid.Should().BeFalse();
}
