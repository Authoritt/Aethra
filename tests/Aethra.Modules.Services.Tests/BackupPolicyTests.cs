using Aethra.Modules.Services.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Tests de <see cref="BackupPolicy.IsValid"/>. Regresión clave: <c>satellite://</c> debe ser válido
/// (antes sólo se aceptaba volume/s3, lo que hacía IMPOSIBLE setear el offload a satélite).
/// </summary>
public sealed class BackupPolicyTests
{
    [Theory]
    [InlineData("volume://default")]
    [InlineData("s3://bucket/prefix")]
    [InlineData("satellite://auto")]            // regresión: antes rechazado → unsettable
    [InlineData("satellite://vm_ABC/backups")]
    public void IsValid_accepts_supported_schemes(string dest)
        => new BackupPolicy("0 3 * * *", 7, dest).IsValid().Should().BeTrue();

    [Theory]
    [InlineData("http://x")]
    [InlineData("ftp://x")]
    [InlineData("nopath")]
    [InlineData("")]
    public void IsValid_rejects_unsupported_or_empty_destination(string dest)
        => new BackupPolicy("0 3 * * *", 7, dest).IsValid().Should().BeFalse();

    [Fact]
    public void IsValid_rejects_empty_cron()
        => new BackupPolicy("", 7, "satellite://auto").IsValid().Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsValid_rejects_nonpositive_retention(int retention)
        => new BackupPolicy("0 3 * * *", retention, "satellite://auto").IsValid().Should().BeFalse();
}
