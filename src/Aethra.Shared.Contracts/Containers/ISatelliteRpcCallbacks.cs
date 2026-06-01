namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Callbacks que el hub central invoca cuando el satélite envía la respuesta a un
/// request RPC. La implementación concreta vive en <c>apps/api</c> (en el mismo
/// componente que <see cref="ISatelliteRpcClient"/>) y resuelve los <c>TaskCompletionSource</c>
/// pendientes por correlation id.
/// <para>
/// Esta interfaz existe para romper la dependencia direccional: el hub de SignalR
/// (en <c>Modules.Vms</c>) no puede referenciar tipos de <c>apps/api</c>. Inyectar
/// <see cref="ISatelliteRpcCallbacks"/> en el hub permite que el host registre la
/// instancia singleton compartida con el cliente RPC.
/// </para>
/// </summary>
public interface ISatelliteRpcCallbacks
{
    /// <summary>Llamado cuando el satélite responde a un Build/Run/List/etc. (operaciones
    /// no-stream). <paramref name="response"/> debe ser una de las records
    /// <c>BuildImageResponse</c>, <c>RunContainerResponse</c>, <c>ListContainersResponse</c>,
    /// o cualquier objeto para operaciones void (Stop/Remove).</summary>
    void CompleteRequest(string correlationId, object response);

    /// <summary>Llamado cuando el satélite indica que un request falló. Propaga la excepción
    /// al consumidor.</summary>
    void FailRequest(string correlationId, Exception error);

    /// <summary>Llamado por cada línea de log del stream identificado por
    /// <see cref="LogChunk.CorrelationId"/>.</summary>
    void PushLogChunk(LogChunk chunk);

    /// <summary>Cierra el stream de logs (EOF normal si <paramref name="error"/> es null,
    /// fallo si trae excepción).</summary>
    void CompleteStream(string correlationId, Exception? error = null);
}
