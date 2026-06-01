namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Credenciales para autenticarse contra un registry de imágenes (Docker Hub, GHCR, registry interno, etc.).
/// </summary>
/// <param name="Server">Hostname (con puerto opcional) del registry, p. ej. <c>localhost:5000</c>.</param>
/// <param name="Username">Usuario.</param>
/// <param name="Password">Password o token. Se envía en claro por SignalR (asumido TLS).</param>
public sealed record RegistryAuth(string Server, string Username, string Password);
