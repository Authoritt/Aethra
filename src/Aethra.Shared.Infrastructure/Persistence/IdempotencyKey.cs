namespace Aethra.Shared.Infrastructure.Persistence;

/// <summary>
/// Persistencia del Idempotency-Key + payload de la respuesta para devolverla a reintentos.
/// Tabla compartida en schema "shared". Una sola tabla porque las keys son strings opacos
/// y el RequestType distingue colisiones entre comandos diferentes.
/// </summary>
public sealed class IdempotencyKey
{
    public string Key { get; set; } = default!;
    public string RequestType { get; set; } = default!;
    public string ResponseJson { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
