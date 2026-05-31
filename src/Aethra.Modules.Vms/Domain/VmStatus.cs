namespace Aethra.Modules.Vms.Domain;

/// <summary>
/// Estado de conectividad de un satélite a su VM.
/// State machine:  Pending → Connected ↔ Disconnected.
/// "Pending" significa que se registró la VM pero el satélite nunca se conectó.
/// </summary>
public enum VmStatus
{
    Pending = 0,
    Connected = 1,
    Disconnected = 2,
}
