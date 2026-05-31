using Aethra.Shared.Infrastructure.Cqrs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Shared.Infrastructure.Pipelines;

/// <summary>
/// Envuelve la ejecucion del handler de un ICommand en una transaccion EF Core.
/// Si el comando falla o lanza, hace rollback. Si tiene exito, commit y publicacion de outbox messages.
///
/// Convenciones:
/// - Solo aplica a ICommand / ICommand&lt;T&gt; — las queries NO entran.
/// - Cada modulo registra su propio DbContext y este behavior se resuelve via DI por modulo
///   (Aethra.Shared.Infrastructure.Pipelines.TransactionBehavior&lt;TRequest, TResponse, TDbContext&gt;).
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse, TDbContext>(
    TDbContext dbContext,
    ILogger<TransactionBehavior<TRequest, TResponse, TDbContext>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TDbContext : DbContext
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Las queries no necesitan transaccion.
        if (request is not ICommand && !IsGenericCommand(request))
        {
            return await next().ConfigureAwait(false);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await next().ConfigureAwait(false);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("Rollback de transaccion en {RequestName}", typeof(TRequest).Name);
                throw;
            }
        }).ConfigureAwait(false);
    }

    private static bool IsGenericCommand(TRequest request)
    {
        var t = request.GetType();
        return t.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
    }
}
