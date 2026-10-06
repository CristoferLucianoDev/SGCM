namespace SGCM.Data.Domain.Common;

/// <summary>
/// Resultado de una operación que puede fallar por una regla de negocio.
/// Reemplaza a las excepciones en los fallos esperados: un resultado exitoso
/// siempre lleva <see cref="Error.None"/> y uno fallido siempre lleva un error real.
/// </summary>
public class Result
{
    /// <summary>
    /// Indica si la operación tuvo éxito.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Indica si la operación falló.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Error de la operación, o <see cref="Error.None"/> si tuvo éxito.
    /// </summary>
    public Error Error { get; }

    /// <summary>
    /// Crea un resultado validando que el estado y el error sean coherentes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Si un éxito trae un error o un fallo no lo trae.
    /// </exception>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("Un resultado exitoso no puede tener error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("Un resultado fallido debe indicar el error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Crea un resultado fallido con el error indicado.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Crea un resultado exitoso.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Permite devolver un <see cref="Error"/> directamente como resultado fallido.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Resultado de una operación que, si tiene éxito, produce un valor.
/// </summary>
/// <typeparam name="T">Tipo del valor producido.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, Error.None)
    {
        _value = value;
    }

    private Result(Error error) : base(false, error)
    {
    }

    /// <summary>
    /// Valor producido por la operación.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si el resultado es fallido.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    /// <summary>Crea un resultado exitoso con el valor indicado.</summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>Crea un resultado fallido con el error indicado.</summary>
    public static new Result<T> Failure(Error error) => new(error);

    /// <summary>Permite devolver el valor directamente como resultado exitoso.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Permite devolver un <see cref="Error"/> directamente como resultado fallido.</summary>
    public static implicit operator Result<T>(Error error) => Failure(error);
}
