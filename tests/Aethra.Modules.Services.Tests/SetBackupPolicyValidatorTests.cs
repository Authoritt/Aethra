using Aethra.Modules.Services.UseCases.Backups;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Tests del <see cref="SetBackupPolicyValidator"/>: esquema de destino soportado (volume/s3/satellite),
/// rango de retención, cron no vacío cuando se provee, y todo-null = desactivar. Corre en el
/// ValidationBehavior antes del handler.
/// </summary>
public sealed class SetBackupPolicyValidatorTests
{
    private static readonly SetBackupPolicyValidator Validator = new();

    private static SetBackupPolicyCommand New(
        string serviceId = "svc_ABC",
        string? cron = "0 3 * * *",
        int? retention = 7,
        string? destination = "satellite://auto")
        => new(serviceId, cron, retention, destination);

    [Theory]
    [InlineData("satellite://auto")]
    [InlineData("satellite://vm_ABC/backups")]
    [InlineData("volume://default")]
    [InlineData("s3://bucket/prefix")]
    public void Accepts_supported_destination_schemes(string dest)
        => Validator.Validate(New(destination: dest)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("satelite://auto")]   // typo: un solo 't'
    [InlineData("ftp://x")]
    [InlineData("just-a-path")]
    public void Rejects_unsupported_destination_scheme(string dest)
        => Validator.Validate(New(destination: dest)).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_empty_service_id()
        => Validator.Validate(New(serviceId: "")).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(400)]
    public void Rejects_retention_out_of_range(int retention)
        => Validator.Validate(New(retention: retention)).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_empty_cron_when_provided()
        => Validator.Validate(New(cron: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Accepts_all_null_to_disable_policy()
        => Validator.Validate(New(cron: null, retention: null, destination: null)).IsValid.Should().BeTrue();
}
