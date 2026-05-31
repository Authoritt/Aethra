using Aethra.Shared.Kernel.Results;
using MediatR;

namespace Aethra.Shared.Infrastructure.Cqrs;

/// <summary>
/// Comando que muta estado. Siempre devuelve <see cref="Result"/> o <see cref="Result{T}"/>
/// — nunca lanza excepciones para flujo esperado.
/// </summary>
public interface ICommand : IRequest<Result>;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;
