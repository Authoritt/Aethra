using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.UseCases.Dtos;

namespace Aethra.Modules.Vms.UseCases.Vms;

internal static class VmMapper
{
    public static VmDto ToDto(Vm v) => new(
        Id: v.Id.ToString(),
        Slug: v.Slug.Value,
        Name: v.Name,
        PublicIp: v.PublicIp,
        PrivateIp: v.PrivateIp,
        Description: v.Description,
        Status: v.Status.ToString(),
        CreatedAt: v.CreatedAt,
        UpdatedAt: v.UpdatedAt,
        LastConnectedAt: v.LastConnectedAt,
        LastDisconnectedAt: v.LastDisconnectedAt,
        Hostname: v.Hostname,
        KernelVersion: v.KernelVersion,
        CpuModel: v.CpuModel,
        CpuCores: v.CpuCores,
        TotalMemoryBytes: v.TotalMemoryBytes,
        AgentVersion: v.Satellite.AgentVersion,
        AcceptsPreviews: v.AcceptsPreviews);
}
