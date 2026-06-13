using Aethra.Modules.Monitoring.UseCases.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Monitoring.Tests;

/// <summary>
/// Tests del validator FluentValidation <c>CreateMonitorValidator</c>: required + límites de
/// longitud de slug/name/url/httpMethod. Puro, sin BD.
/// </summary>
public sealed class MonitoringValidatorTests
{
    private static CreateMonitorCommand New(
        string slug = "uptime", string name = "Uptime",
        string url = "https://app.example.com/health", string method = "GET")
        => new(slug, name, url, method, null, null, null, null, null, null, null);

    [Fact]
    public void CreateMonitor_accepts_a_valid_command()
        => new CreateMonitorValidator().Validate(New()).IsValid.Should().BeTrue();

    [Fact]
    public void CreateMonitor_requires_slug_name_url_and_http_method()
    {
        new CreateMonitorValidator().Validate(New(slug: "")).IsValid.Should().BeFalse();
        new CreateMonitorValidator().Validate(New(name: "")).IsValid.Should().BeFalse();
        new CreateMonitorValidator().Validate(New(url: "")).IsValid.Should().BeFalse();
        new CreateMonitorValidator().Validate(New(method: "")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateMonitor_rejects_slug_over_64_chars()
        => new CreateMonitorValidator().Validate(New(slug: new string('s', 65))).IsValid.Should().BeFalse();

    [Fact]
    public void CreateMonitor_rejects_name_over_255_chars()
        => new CreateMonitorValidator().Validate(New(name: new string('n', 256))).IsValid.Should().BeFalse();

    [Fact]
    public void CreateMonitor_rejects_url_over_2048_chars()
        => new CreateMonitorValidator().Validate(New(url: "https://" + new string('a', 2050))).IsValid.Should().BeFalse();
}
