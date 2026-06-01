namespace Aethra.Shared.Contracts.Containers;

/// <summary>Resultado de arrancar un contenedor a partir de un <see cref="RunSpec"/>.</summary>
/// <param name="Success">True si el contenedor quedó corriendo.</param>
/// <param name="ContainerId">ID asignado por el runtime al contenedor creado.</param>
/// <param name="ErrorMessage">Mensaje del fallo si no fue exitoso.</param>
public sealed record RunResult(bool Success, string? ContainerId, string? ErrorMessage);
