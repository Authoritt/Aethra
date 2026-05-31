namespace Aethra.Shared.Infrastructure.Cqrs;

/// <summary>
/// Marker para comandos que aceptan Idempotency-Key.
/// IdempotencyBehavior cachea la respuesta por hash(IdempotencyKey + tipo de comando)
/// y la devuelve sin re-ejecutar el handler.
/// </summary>
public interface IIdempotentCommand
{
    string IdempotencyKey { get; }
}
