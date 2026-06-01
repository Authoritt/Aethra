namespace Aethra.Shared.Contracts.Containers;

/// <summary>Resultado de un push de imagen a un registry remoto.</summary>
/// <param name="Success">True si todos los layers se subieron correctamente.</param>
/// <param name="Digest">Digest <c>sha256:...</c> que devolvió el registry tras aceptar el push.</param>
/// <param name="ErrorMessage">Mensaje del fallo si no fue exitoso.</param>
public sealed record PushResult(bool Success, string? Digest, string? ErrorMessage);
