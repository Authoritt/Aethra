namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Lanzada por <see cref="ISatelliteRpcClient"/> cuando se intenta enviar un comando a un
/// <c>vmId</c> que no tiene satélite conectado al hub central. Los orquestadores la atrapan
/// y mapean a un fallo con <c>errorCode = "no_satellite"</c>.
/// </summary>
public sealed class SatelliteNotConnectedException : InvalidOperationException
{
    public string VmId { get; }

    public SatelliteNotConnectedException(string vmId)
        : base($"No hay satélite conectado para vmId='{vmId}'.")
    {
        VmId = vmId;
    }

    public SatelliteNotConnectedException(string vmId, string message)
        : base(message)
    {
        VmId = vmId;
    }
}
