namespace Aethra.Modules.Vms.Authentication;

public static class SatelliteAuthSchemes
{
    public const string TokenHeader = "X-Satellite-Token";
    public const string QueryParam = "access_token";

    /// <summary>
    /// Tipo de claim que pone el hub para que los handlers conozcan al satélite autenticado.
    /// </summary>
    public const string VmIdClaim = "aethra:vm_id";
}
