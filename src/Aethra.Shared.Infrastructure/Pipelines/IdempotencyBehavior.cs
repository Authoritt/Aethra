using System.Text.Json;
using Aethra.Shared.Infrastructure.Cqrs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aethra.Shared.Infrastructure.Pipelines;

/// <summary>
/// Si el request implementa <see cref="IIdempotentCommand"/>, busca por (key, request type) en el store.
/// Hit: devuelve la respuesta cacheada sin ejecutar el handler.
/// Miss: ejecuta el handler y persiste la respuesta serializada (TTL configurable, default 24h).
///
/// El TransactionBehavior debe registrarse DESPUES de este (orden DI) para que la escritura
/// del cache caiga dentro de la misma transaccion que el comando.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyStore store,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand idem || string.IsNullOrWhiteSpace(idem.IdempotencyKey))
        {
            return await next().ConfigureAwait(false);
        }

        var key = idem.IdempotencyKey;
        var typeName = typeof(TRequest).FullName ?? typeof(TRequest).Name;

        var cached = await store.TryGetAsync(key, typeName, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            logger.LogInformation("Idempotency HIT key={Key} type={Type}", key, typeName);
            var hit = JsonSerializer.Deserialize<TResponse>(cached);
            if (hit is not null)
            {
                return hit;
            }
        }

        var response = await next().ConfigureAwait(false);
        var json = JsonSerializer.Serialize(response);
        await store.SaveAsync(key, typeName, json, DefaultTtl, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Idempotency SAVE key={Key} type={Type}", key, typeName);
        return response;
    }
}
