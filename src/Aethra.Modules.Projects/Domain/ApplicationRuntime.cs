using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain;

/// <summary>
/// Cómo corre la <see cref="Application"/> tras el deploy: en qué VM, qué nombre de contenedor,
/// puertos expuestos, volúmenes y healthcheck.
/// </summary>
public sealed class ApplicationRuntime
{
    public string TargetVmId { get; private set; }
    public ContainerName ContainerName { get; private set; }
    public IReadOnlyList<PortMapping> Ports { get; private set; }
    public IReadOnlyList<VolumeMount> Volumes { get; private set; }
    public Healthcheck? Healthcheck { get; private set; }

    private ApplicationRuntime(
        string targetVmId,
        ContainerName containerName,
        IReadOnlyList<PortMapping> ports,
        IReadOnlyList<VolumeMount> volumes,
        Healthcheck? healthcheck)
    {
        TargetVmId = targetVmId;
        ContainerName = containerName;
        Ports = ports;
        Volumes = volumes;
        Healthcheck = healthcheck;
    }

    public static ApplicationRuntime Create(
        string targetVmId,
        ContainerName containerName,
        IReadOnlyList<PortMapping>? ports = null,
        IReadOnlyList<VolumeMount>? volumes = null,
        Healthcheck? healthcheck = null)
        => new(targetVmId, containerName, ports ?? [], volumes ?? [], healthcheck);

    public void UpdatePorts(IReadOnlyList<PortMapping> ports) => Ports = ports;
    public void UpdateVolumes(IReadOnlyList<VolumeMount> volumes) => Volumes = volumes;
    public void UpdateHealthcheck(Healthcheck? hc) => Healthcheck = hc;
    public void UpdateContainerName(ContainerName containerName) => ContainerName = containerName;
    public void UpdateTargetVm(string vmId) => TargetVmId = vmId;

    // EF Core
    private ApplicationRuntime() : this(string.Empty, default!, [], [], null) { }
}

/// <summary>
/// <c>ContainerPort</c>: puerto expuesto por el proceso dentro del contenedor.
/// <c>HostPort</c>: puerto en el host (opcional). Si es null, YARP usa solo ContainerPort por la red Docker.
/// </summary>
public sealed record PortMapping(Port ContainerPort, int? HostPort, string Protocol = "tcp");

/// <summary>
/// <c>HostPath</c>: ruta o nombre de volumen Docker. <c>ContainerPath</c>: punto de montaje.
/// </summary>
public sealed record VolumeMount(string HostPath, string ContainerPath, bool ReadOnly = false);

public sealed record Healthcheck(
    string[] Cmd,
    TimeSpan Interval,
    TimeSpan Timeout,
    int Retries,
    TimeSpan StartPeriod
);
