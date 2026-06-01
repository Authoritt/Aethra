using Aethra.Shared.Contracts.Containers;

namespace Aethra.Api.Hubs;

/// <summary>
/// Implementación stub de <see cref="ISatelliteRpcClient"/> usada en F9.2 antes de
/// que el central tenga el correlation tracking real (F9.3). Toda llamada lanza
/// <see cref="NotImplementedException"/>; los call sites que necesiten ejercitar
/// el flujo de deploy deben inyectar un fake/mock en sus tests hasta que F9.3 cierre.
/// </summary>
public sealed class NotImplementedSatelliteRpcClient : ISatelliteRpcClient
{
    private const string Pending = "ISatelliteRpcClient: implementación real se entrega en F9.3.";

    public Task<BuildResult> SendBuildAsync(string vmId, BuildSpec spec, RegistryAuth? pushTo, CancellationToken ct)
        => throw new NotImplementedException(Pending);

    public Task<RunResult> SendRunAsync(string vmId, RunSpec spec, RegistryAuth? pullFrom, CancellationToken ct)
        => throw new NotImplementedException(Pending);

    public Task SendStopAsync(string vmId, string containerNameOrId, CancellationToken ct)
        => throw new NotImplementedException(Pending);

    public Task SendRemoveAsync(string vmId, string containerNameOrId, bool force, CancellationToken ct)
        => throw new NotImplementedException(Pending);

    public IAsyncEnumerable<LogChunk> StreamLogsAsync(
        string vmId, string containerNameOrId, int tailLines, CancellationToken ct)
        => ThrowAsync();

    private static async IAsyncEnumerable<LogChunk> ThrowAsync()
    {
        // El throw asegura que cualquier consumidor reciba el error en cuanto haga MoveNextAsync.
        await Task.Yield();
        throw new NotImplementedException(Pending);
#pragma warning disable CS0162 // unreachable: necesario para que el método sea un iterador (yield).
        yield break;
#pragma warning restore CS0162
    }

    public Task<IReadOnlyList<ContainerInfo>> SendListContainersAsync(string vmId, CancellationToken ct)
        => throw new NotImplementedException(Pending);
}
