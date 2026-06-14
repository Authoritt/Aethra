using Aethra.Modules.Metrics.UseCases.Queries;
using Aethra.Shared.Contracts.Vms;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Metrics.Tests;

/// <summary>
/// <see cref="MetricsDiskAggregator.Aggregate(System.Collections.Generic.IReadOnlyList{DiskUsage})"/>
/// suma SÓLO disco real, excluyendo filesystems virtuales/RAM (tmpfs, overlay, devtmpfs, ...).
/// Regresión del bug reportado: la VM principal mostraba ~125 GB / 63 GB libres porque sumaba 4 tmpfs
/// (respaldados por RAM); el disco real es ~97 GB (root ext4 + /boot).
/// </summary>
public sealed class MetricsDiskAggregatorTests
{
    private const long GB = 1024L * 1024 * 1024;

    private static DiskUsage Disk(string fs, long totalGb, long usedGb) => new(
        MountPoint: "/x",
        Filesystem: fs,
        TotalBytes: totalGb * GB,
        UsedBytes: usedGb * GB,
        AvailableBytes: (totalGb - usedGb) * GB);

    [Fact]
    public void Excludes_tmpfs_and_virtual_filesystems()
    {
        var disks = new List<DiskUsage>
        {
            Disk("ext4", 96, 57),     // root real
            Disk("vfat", 1, 0),       // /boot/efi real
            Disk("tmpfs", 12, 0),     // RAM — excluir
            Disk("tmpfs", 12, 0),     // RAM — excluir
            Disk("overlay", 96, 57),  // overlay de contenedor — excluir
            Disk("devtmpfs", 4, 0),   // RAM — excluir
        };

        var (used, total) = MetricsDiskAggregator.Aggregate(disks);

        total.Should().Be(97 * GB); // sólo ext4(96) + vfat(1)
        used.Should().Be(57 * GB);
    }

    [Fact]
    public void Sums_multiple_real_disks()
    {
        var (used, total) = MetricsDiskAggregator.Aggregate(
            [Disk("ext4", 50, 10), Disk("xfs", 30, 5)]);
        total.Should().Be(80 * GB);
        used.Should().Be(15 * GB);
    }

    [Fact]
    public void Filesystem_match_is_case_insensitive()
    {
        var (_, total) = MetricsDiskAggregator.Aggregate(
            [Disk("ext4", 50, 10), Disk("TMPFS", 12, 0)]);
        total.Should().Be(50 * GB);
    }

    [Fact]
    public void Null_or_empty_returns_zero()
    {
        MetricsDiskAggregator.Aggregate((IReadOnlyList<DiskUsage>?)null).Should().Be((0L, 0L));
        MetricsDiskAggregator.Aggregate([]).Should().Be((0L, 0L));
    }
}
