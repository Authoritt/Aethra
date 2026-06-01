namespace Aethra.Shared.Contracts.Containers;

/// <summary>Resultado de un pull de imagen desde un registry.</summary>
/// <param name="Success">True si la imagen quedó disponible localmente.</param>
/// <param name="ImageId">SHA local de la imagen tras el pull.</param>
/// <param name="ErrorMessage">Mensaje del fallo si no fue exitoso.</param>
public sealed record PullResult(bool Success, string? ImageId, string? ErrorMessage);
