namespace Aethra.Shared.Contracts.Containers;

/// <summary>Vista resumida de un contenedor conocido por el runtime local.</summary>
/// <param name="Id">ID del contenedor (sha resumido o completo según runtime).</param>
/// <param name="Name">Nombre humano del contenedor.</param>
/// <param name="Image">Referencia de la imagen que ejecuta.</param>
/// <param name="Status">Estado textual reportado por el runtime (<c>running</c>, <c>exited (0)</c>, etc.).</param>
/// <param name="ExposedPorts">Puertos internos del contenedor expuestos al host.</param>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Status,
    IReadOnlyList<int> ExposedPorts);
