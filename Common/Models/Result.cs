namespace APCS.Common.Models;

/// <summary>
/// Represents the result of an operation that can fail with an expected error.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot contain an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must contain an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the expected error when the operation failed.
    /// </summary>
    public Error Error { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The expected error.</param>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="value">The result value.</param>
    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    /// <summary>
    /// Creates a failed result with a value type.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="error">The expected error.</param>
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);
}

/// <summary>
/// Represents the result of an operation that returns a value.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value)
        : base(true, Error.None)
    {
        _value = value;
    }

    private Result(Error error)
        : base(false, error)
    {
    }

    /// <summary>
    /// Gets the result value when the operation succeeded.
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The result value.</param>
    public static Result<TValue> Success(TValue value) => new(value);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The expected error.</param>
    public new static Result<TValue> Failure(Error error) => new(error);
}
