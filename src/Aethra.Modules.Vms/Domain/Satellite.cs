using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Vms.Domain;

/// <summary>
/// Agente que corre dentro de la VM y reporta métricas + ejecuta comandos.
/// Por ahora 1:1 con su <see cref="Vm"/>.
/// </summary>
public sealed class Satellite : Entity<SatelliteId>
{
    public SatelliteToken Token { get; private set; }
    public string? AgentVersion { get; private set; }
    public DateTimeOffset? LastHandshakeAt { get; private set; }

    internal Satellite(SatelliteId id, SatelliteToken token) : base(id)
    {
        Token = token;
    }

    internal void ReplaceToken(SatelliteToken newToken) => Token = newToken;

    internal void RecordHandshake(string agentVersion, DateTimeOffset now)
    {
        AgentVersion = agentVersion;
        LastHandshakeAt = now;
    }

    // EF Core
    private Satellite() : base() { Token = default!; }
}
