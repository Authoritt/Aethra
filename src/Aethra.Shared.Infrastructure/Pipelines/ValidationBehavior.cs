using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using FluentValidation;
using MediatR;

namespace Aethra.Shared.Infrastructure.Pipelines;

/// <summary>
/// Ejecuta validators FluentValidation registrados para el request.
/// Si alguno falla, corto-circuita devolviendo un <see cref="Result"/> con <see cref="ErrorType.Validation"/>
/// — el handler nunca se ejecuta.
///
/// Soporta TResponse = Result o Result&lt;T&gt;.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)))
                .ConfigureAwait(false))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var error = Error.Validation(
            code: "validation.failed",
            message: string.Join(" | ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));

        return CreateFailureResult(error);
    }

    private static TResponse CreateFailureResult(Error error)
    {
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = responseType.GetMethod(nameof(Result<int>.Failure))
                ?? throw new InvalidOperationException($"No se encontro Result<T>.Failure en {responseType}");
            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"ValidationBehavior requiere TResponse = Result o Result<T>. Recibido: {responseType.Name}");
    }
}
