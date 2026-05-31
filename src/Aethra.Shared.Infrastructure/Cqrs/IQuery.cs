using Aethra.Shared.Kernel.Results;
using MediatR;

namespace Aethra.Shared.Infrastructure.Cqrs;

/// <summary>
/// Query de solo lectura. Devuelve <see cref="Result{T}"/>.
/// Handlers de query NO deben mutar estado.
/// </summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
