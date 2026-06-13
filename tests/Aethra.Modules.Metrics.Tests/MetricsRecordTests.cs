using Aethra.Modules.Metrics.Domain;
using Aethra.Shared.Contracts.Vms;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Metrics.Tests;

/// <summary>
/// Los records de Metrics son time-series append-only construidos vía <c>FromSnapshot</c>. Cubrimos
/// el mapeo de campos, el flatten del <see cref="NetworkSnapshot"/>, la derivación de
/// ContainerCount y la serialización JSON de discos/contenedores.
/// </summary>
public sealed class MetricsRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VmMetricRecord_FromSnapshot_maps_fields_flattens_network_and_serializes_disks()
    {
        var snapshot = new VmMetricSnapshot(
            Now, CpuPercent: 42.5, LoadAverage1: 1.0, LoadAverage5: 2.0, LoadAverage15: 3.0,
            MemoryUsedBytes: 100, MemoryFreeBytes: 200, MemoryTotalBytes: 300,
            SwapUsedBytes: 10, SwapTotalBytes: 20,
            Disks: [new DiskUsage("/", "ext4", 1000, 400, 600)],
            Network: new NetworkSnapshot(11, 22, 33, 44),
            UptimeSeconds: 99999);

        var record = VmMetricRecord.FromSnapshot("vm_1", snapshot);

        record.VmId.Should().Be("vm_1");
        record.Timestamp.Should().Be(Now);
        record.CpuPercent.Should().Be(42.5);
        record.MemoryTotalBytes.Should().Be(300);
        record.NetBytesReceived.Should().Be(11);
        record.NetBytesSent.Should().Be(22);
        record.NetPacketsReceived.Should().Be(33);
        record.NetPacketsSent.Should().Be(44);
        record.UptimeSeconds.Should().Be(99999);
        record.DisksJson.Should().Contain("ext4");
    }

    [Fact]
    public void ContainerSnapshotRecord_FromSnapshot_counts_and_serializes_containers()
    {
        var snapshot = new ContainerListSnapshot(Now,
        [
            new ContainerInfo("id1", "app", "img:1", "Up", "running", Now, [8080]),
            new ContainerInfo("id2", "db", "pg:16", "Up", "running", Now, [5432]),
        ]);

        var record = ContainerSnapshotRecord.FromSnapshot("vm_1", snapshot);

        record.VmId.Should().Be("vm_1");
        record.Timestamp.Should().Be(Now);
        record.ContainerCount.Should().Be(2);
        record.ContainersJson.Should().Contain("app");
    }

    [Fact]
    public void ContainerSnapshotRecord_FromSnapshot_with_no_containers_is_zero_and_empty_array()
    {
        var snapshot = new ContainerListSnapshot(Now, []);

        var record = ContainerSnapshotRecord.FromSnapshot("vm_1", snapshot);

        record.ContainerCount.Should().Be(0);
        record.ContainersJson.Should().Be("[]");
    }
}
