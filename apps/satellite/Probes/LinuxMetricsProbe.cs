using System.Globalization;
using Aethra.Shared.Contracts.Vms;

namespace Aethra.Satellite.Probes;

/// <summary>
/// Probe Linux: lee directamente <c>/proc/*</c> para CPU, memoria, load, red.
/// No requiere dependencias externas (estilo gopsutil, mismo patrón que Beszel).
///
/// Si /proc no existe (Windows), constructor lanza — el host deberá fallback a
/// <see cref="CrossPlatformMetricsProbe"/>.
/// </summary>
public sealed class LinuxMetricsProbe : IMetricsProbe
{
    private readonly string _procPath;
    private (ulong total, ulong idle) _lastCpu;

    public LinuxMetricsProbe(string procPath = "/proc")
    {
        if (!Directory.Exists(procPath))
        {
            throw new InvalidOperationException($"{procPath} no existe. Usar CrossPlatformMetricsProbe en este OS.");
        }
        _procPath = procPath;
    }

    public Task<SatelliteHandshake> HandshakeAsync(CancellationToken ct)
    {
        var cpuModel = ReadFirstLine("/proc/cpuinfo", line => line.StartsWith("model name", StringComparison.Ordinal))
            ?.Split(':')[^1].Trim() ?? "unknown";
        var cores = File.ReadLines(Path.Combine(_procPath, "cpuinfo"))
            .Count(l => l.StartsWith("processor", StringComparison.Ordinal));
        var kernel = TryRead("/proc/sys/kernel/osrelease")?.Trim() ?? Environment.OSVersion.VersionString;
        var totalMem = ReadMemInfoBytes("MemTotal");
        var rootDisk = ReadRootDisk();

        return Task.FromResult(new SatelliteHandshake(
            Hostname: Environment.MachineName,
            KernelVersion: kernel,
            CpuModel: cpuModel,
            CpuCores: cores > 0 ? cores : Environment.ProcessorCount,
            TotalMemoryBytes: totalMem,
            AgentVersion: typeof(LinuxMetricsProbe).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            RootDiskTotalBytes: rootDisk?.total,
            RootDiskAvailableBytes: rootDisk?.available));
    }

    public Task<VmMetricSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cpu = SampleCpu();
        var (loads1, loads5, loads15) = ReadLoadAvg();
        var (memTotal, memFree, memAvailable, swapTotal, swapFree) = ReadFullMemInfo();
        var (rx, tx, rxPkts, txPkts) = ReadNetTotals();
        var uptime = ReadUptimeSeconds();

        var disks = new List<DiskUsage>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Ram))
                {
                    continue;
                }
                disks.Add(new DiskUsage(
                    MountPoint: d.RootDirectory.FullName,
                    Filesystem: d.DriveFormat,
                    TotalBytes: d.TotalSize,
                    UsedBytes: d.TotalSize - d.AvailableFreeSpace,
                    AvailableBytes: d.AvailableFreeSpace));
            }
            catch (IOException) { /* drive temporal indisponible */ }
            catch (UnauthorizedAccessException) { /* sin permiso */ }
        }

        return Task.FromResult(new VmMetricSnapshot(
            Timestamp: now,
            CpuPercent: cpu,
            LoadAverage1: loads1,
            LoadAverage5: loads5,
            LoadAverage15: loads15,
            MemoryUsedBytes: memTotal - memAvailable,
            MemoryFreeBytes: memFree,
            MemoryTotalBytes: memTotal,
            SwapUsedBytes: swapTotal - swapFree,
            SwapTotalBytes: swapTotal,
            Disks: disks,
            Network: new NetworkSnapshot(rx, tx, rxPkts, txPkts),
            UptimeSeconds: uptime));
    }

    private double SampleCpu()
    {
        var firstLine = File.ReadLines(Path.Combine(_procPath, "stat")).First();
        // formato: "cpu  user nice system idle iowait irq softirq steal guest guest_nice"
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ulong total = 0;
        for (int i = 1; i < parts.Length; i++)
        {
            total += ulong.Parse(parts[i], CultureInfo.InvariantCulture);
        }
        var idle = ulong.Parse(parts[4], CultureInfo.InvariantCulture);

        if (_lastCpu.total == 0)
        {
            _lastCpu = (total, idle);
            return 0;
        }
        var totalDelta = total - _lastCpu.total;
        var idleDelta = idle - _lastCpu.idle;
        _lastCpu = (total, idle);
        return totalDelta == 0 ? 0 : (1.0 - (double)idleDelta / totalDelta) * 100.0;
    }

    private (double, double, double) ReadLoadAvg()
    {
        var content = File.ReadAllText(Path.Combine(_procPath, "loadavg")).Trim();
        var parts = content.Split(' ');
        return (
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private (long Total, long Free, long Available, long SwapTotal, long SwapFree) ReadFullMemInfo()
    {
        long total = 0, free = 0, available = 0, swapTotal = 0, swapFree = 0;
        foreach (var line in File.ReadLines(Path.Combine(_procPath, "meminfo")))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = KbToBytes(line);
            }
            else if (line.StartsWith("MemFree:", StringComparison.Ordinal))
            {
                free = KbToBytes(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = KbToBytes(line);
            }
            else if (line.StartsWith("SwapTotal:", StringComparison.Ordinal))
            {
                swapTotal = KbToBytes(line);
            }
            else if (line.StartsWith("SwapFree:", StringComparison.Ordinal))
            {
                swapFree = KbToBytes(line);
            }
        }
        return (total, free, available, swapTotal, swapFree);
    }

    private long ReadMemInfoBytes(string key)
    {
        foreach (var line in File.ReadLines(Path.Combine(_procPath, "meminfo")))
        {
            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                return KbToBytes(line);
            }
        }
        return 0;
    }

    private static long KbToBytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(parts[1], CultureInfo.InvariantCulture) * 1024L;
    }

    private (long Rx, long Tx, long RxPkts, long TxPkts) ReadNetTotals()
    {
        long rx = 0, tx = 0, rxPkts = 0, txPkts = 0;
        // /proc/net/dev: skip header (primeras 2 líneas), suma interfaces excluyendo lo
        foreach (var line in File.ReadLines(Path.Combine(_procPath, "net/dev")).Skip(2))
        {
            var sepIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (sepIndex < 0)
            {
                continue;
            }
            var iface = line[..sepIndex].Trim();
            if (iface == "lo")
            {
                continue;
            }
            var nums = line[(sepIndex + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rx += long.Parse(nums[0], CultureInfo.InvariantCulture);
            rxPkts += long.Parse(nums[1], CultureInfo.InvariantCulture);
            tx += long.Parse(nums[8], CultureInfo.InvariantCulture);
            txPkts += long.Parse(nums[9], CultureInfo.InvariantCulture);
        }
        return (rx, tx, rxPkts, txPkts);
    }

    private double ReadUptimeSeconds()
    {
        var content = File.ReadAllText(Path.Combine(_procPath, "uptime")).Trim();
        var first = content.Split(' ')[0];
        return double.Parse(first, CultureInfo.InvariantCulture);
    }

    private string? ReadFirstLine(string relPath, Func<string, bool> predicate)
    {
        var path = Path.Combine(_procPath, relPath.TrimStart('/').Replace("proc/", ""));
        foreach (var line in File.ReadLines(path))
        {
            if (predicate(line))
            {
                return line;
            }
        }
        return null;
    }

    private string? TryRead(string relPath)
    {
        var path = Path.Combine(_procPath, relPath.TrimStart('/').Replace("proc/", ""));
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    private static (long total, long available)? ReadRootDisk()
    {
        try
        {
            var root = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Ram)
                .OrderByDescending(d => d.RootDirectory.FullName == "/" ? 1 : 0)
                .FirstOrDefault();
            return root is null ? null : (root.TotalSize, root.AvailableFreeSpace);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
