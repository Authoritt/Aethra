using Aethra.Modules.Monitoring.UseCases.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Monitoring.Tests;

/// <summary>
/// Tests del <see cref="UpdateMonitorValidator"/>: semántica PATCH — sólo valida los campos provistos
/// (no-null), espejando las reglas de CreateMonitor. Puro, sin BD.
/// </summary>
public sealed class UpdateMonitorValidatorTests
{
    private static UpdateMonitorCommand New(
        string monitorId = "mon_ABC",
        string? name = null,
        string? url = null,
        string? httpMethod = null)
        => new(monitorId, name, url, httpMethod, null, null, null, null, false, null, false, null, false, null, false);

    private static readonly UpdateMonitorValidator Validator = new();

    [Fact]
    public void Accepts_a_noop_patch_only_id()
        => Validator.Validate(New()).IsValid.Should().BeTrue();

    [Fact]
    public void Accepts_valid_provided_fields()
        => Validator.Validate(New(name: "Uptime", url: "https://app.example.com/health", httpMethod: "GET"))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Rejects_empty_monitor_id()
        => Validator.Validate(New(monitorId: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_provided_empty_name()
        => Validator.Validate(New(name: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_name_over_255()
        => Validator.Validate(New(name: new string('a', 256))).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_provided_empty_url()
        => Validator.Validate(New(url: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_url_over_2048()
        => Validator.Validate(New(url: "https://x/" + new string('a', 2048))).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_provided_empty_http_method()
        => Validator.Validate(New(httpMethod: "")).IsValid.Should().BeFalse();
}
