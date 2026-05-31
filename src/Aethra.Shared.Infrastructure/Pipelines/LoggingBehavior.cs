using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aethra.Shared.Infrastructure.Pipelines;

/// <summary>
/// Loguea cada request: nombre, duracion, exito/fallo.
/// Usa scopes estructurados para que Serilog enriquezca con el nombre del request.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestName"] = name });

        var sw = Stopwatch.StartNew();
        logger.LogInformation("→ {RequestName} iniciado", name);
        try
        {
            var response = await next().ConfigureAwait(false);
            sw.Stop();
            logger.LogInformation("← {RequestName} completado en {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "✗ {RequestName} fallo tras {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
