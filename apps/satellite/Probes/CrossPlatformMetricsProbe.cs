using System.Diagnostics;
using System.Runtime.InteropServices;
using Aethra.Shared.Contracts.Vms;

namespace Aethra.Satellite.Probes;

/// <summary>
/// Probe que usa solo BCL — funciona en Windows, Linux y macOS pero con métricas limitadas
/// (no hay load average en BCL puro, no hay packets contadores en Windows sin WMI).
///
/// Para producción en Linux usar <see cref="LinuxMetricsProbe"/> que lee /proc directamente.
/// </summary>
public sealed class CrossPlatformMetricsProbe : IMetricsProbe
{
    private readonly Process _self = Process.GetCurrentProcess();
    private TimeSpan _lastCpuTime;
    private DateTimeOffset _lastSampleAt;

    public Task<SatelliteHandshake> HandshakeAsync(CancellationToken ct)
    {
        var rootDisk = ReadRootDisk();
        var info = new SatelliteHandshake(
            Hostname: Environment.MachineName,
            KernelVersion: RuntimeInformation.OSDescription,
            CpuModel: RuntimeInformation.ProcessArchitecture.ToString(),
            CpuCores: Environment.ProcessorCount,
            TotalMemoryBytes: GetTotalMemoryBytes(),
            AgentVersion: typeof(CrossPlatformMetricsProbe).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            RootDiskTotalBytes: rootDisk?.total,
            RootDiskAvailableBytes: rootDisk?.available);
        return Task.FromResult(info);
    }

    public Task<VmMetricSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cpu = SampleCpuPercent(now);
        var totalMem = GetTotalMemoryBytes();
        var workingSet = GC.GetTotalMemory(forceFullCollection: false);
        var processMem = Environment.WorkingSet;
        var freeMem = Math.Max(totalMem - processMem, 0);

        var disks = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed)
            .Select(d => new DiskUsage(
                MountPoint: d.RootDirectory.FullName,
                Filesystem: d.DriveFormat,
                TotalBytes: d.TotalSize,
                UsedBytes: d.TotalSize - d.AvailableFreeSpace,
                AvailableBytes: d.AvailableFreeSpace))
            .ToList();

        var snapshot = new VmMetricSnapshot(
            Timestamp: now,
            CpuPercent: cpu,
            LoadAverage1: 0,
            LoadAverage5: 0,
            LoadAverage15: 0,
            MemoryUsedBytes: processMem,
            MemoryFreeBytes: freeMem,
            MemoryTotalBytes: totalMem,
            SwapUsedBytes: 0,
            SwapTotalBytes: 0,
            Disks: disks,
            Network: new NetworkSnapshot(0, 0, 0, 0),
            UptimeSeconds: Environment.TickCount64 / 1000.0);

        _ = workingSet;
        return Task.FromResult(snapshot);
    }

    private double SampleCpuPercent(DateTimeOffset now)
    {
        _self.Refresh();
        var currentCpuTime = _self.TotalProcessorTime;
        if (_lastSampleAt == default)
        {
            _lastCpuTime = currentCpuTime;
            _lastSampleAt = now;
            return 0;
        }
        var elapsedCpu = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
        var elapsedWall = (now - _lastSampleAt).TotalMilliseconds;
        _lastCpuTime = currentCpuTime;
        _lastSampleAt = now;
        if (elapsedWall <= 0)
        {
            return 0;
        }
        var cpu = elapsedCpu / (elapsedWall * Environment.ProcessorCount) * 100.0;
        return Math.Clamp(cpu, 0, 100);
    }

    private static long GetTotalMemoryBytes()
    {
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes > 0
            ? info.TotalAvailableMemoryBytes
            : Environment.WorkingSet * 4;
    }

    private static (long total, long available)? ReadRootDisk()
    {
        try
        {
            var rootPath = Path.GetPathRoot(Environment.CurrentDirectory);
            var root = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType is DriveType.Fixed)
                .OrderByDescending(d => string.Equals(d.RootDirectory.FullName, rootPath, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            return root is null ? null : (root.TotalSize, root.AvailableFreeSpace);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
