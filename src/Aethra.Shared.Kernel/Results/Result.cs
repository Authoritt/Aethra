using Aethra.Shared.Kernel.Errors;

namespace Aethra.Shared.Kernel.Results;

/// <summary>
/// Resultado de una operación sin payload. Éxito o error explícito.
/// Use <see cref="Result{T}"/> cuando el caso de éxito devuelve un valor.
/// </summary>
public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Un Result exitoso no puede tener un Error.");
        }
        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Un Result fallido requiere un Error.");
        }
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Resultado tipado: éxito con un <typeparamref name="T"/> o error explícito.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede acceder al Value de un Result fallido.");

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
        Error = Error.None;
    }

    private Result(Error error)
    {
        _value = default;
        IsSuccess = false;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(Error);

    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        => IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(Error);

    public async Task<Result<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> map)
        => IsSuccess ? Result<TOut>.Success(await map(_value!).ConfigureAwait(false)) : Result<TOut>.Failure(Error);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
        => IsSuccess ? bind(_value!) : Result<TOut>.Failure(Error);
}
