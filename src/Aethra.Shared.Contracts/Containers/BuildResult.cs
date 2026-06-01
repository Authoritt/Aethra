namespace Aethra.Shared.Contracts.Containers;

/// <summary>Resultado de una operación de build de imagen.</summary>
/// <param name="Success">True si la imagen quedó construida correctamente.</param>
/// <param name="ImageId">SHA del image ID final si <paramref name="Success"/> es true; null en error.</param>
/// <param name="ErrorMessage">Mensaje legible del fallo si <paramref name="Success"/> es false.</param>
/// <param name="LogLines">Líneas del log de build (stdout/stderr del builder) en orden cronológico.</param>
public sealed record BuildResult(
    bool Success,
    string? ImageId,
    string? ErrorMessage,
    IReadOnlyList<string> LogLines);
