using Aethra.Modules.Services.Infrastructure.Backup;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Routing de URIs de <see cref="SatelliteBackupStorage"/>: ParseDestination (al escribir → target+sub),
/// ParseFull (al leer/borrar → vmId+relativePath), CombineRelative. Garantiza que el backup va y vuelve
/// del satélite correcto y con el path correcto.
/// </summary>
public sealed class SatelliteBackupStorageTests
{
    [Theory]
    [InlineData("satellite://auto", "auto", "backups")]
    [InlineData("satellite://auto/", "auto", "backups")]
    [InlineData("satellite://vm_ABC", "vm_ABC", "backups")]
    [InlineData("satellite://vm_ABC/pg", "vm_ABC", "pg")]
    [InlineData("satellite://vm_ABC/a/b", "vm_ABC", "a/b")]
    [InlineData("auto", "auto", "backups")]
    public void ParseDestination_extracts_target_and_sub(string dest, string target, string sub)
    {
        var (t, s) = SatelliteBackupStorage.ParseDestination(dest);
        t.Should().Be(target);
        s.Should().Be(sub);
    }

    [Theory]
    [InlineData("satellite://vm_ABC/backups/file.gz", "vm_ABC", "backups/file.gz")]
    [InlineData("satellite://vm_X/a/b/c.gz", "vm_X", "a/b/c.gz")]
    public void ParseFull_extracts_vmId_and_relativePath(string full, string vmId, string rel)
    {
        var (v, r) = SatelliteBackupStorage.ParseFull(full);
        v.Should().Be(vmId);
        r.Should().Be(rel);
    }

    [Fact]
    public void ParseFull_throws_when_no_path()
    {
        var act = () => SatelliteBackupStorage.ParseFull("satellite://vm_ABC");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("backups", "f.gz", "backups/f.gz")]
    [InlineData("", "f.gz", "f.gz")]
    [InlineData("a/b", "f.gz", "a/b/f.gz")]
    public void CombineRelative_joins(string sub, string file, string expected)
        => SatelliteBackupStorage.CombineRelative(sub, file).Should().Be(expected);
}
