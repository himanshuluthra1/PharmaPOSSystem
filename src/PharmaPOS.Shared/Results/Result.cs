namespace PharmaPOS.Shared.Results;

/// <summary>
/// Represents the outcome of an operation, encapsulating success/failure state
/// and an optional error message. Used across all layers to avoid throwing for
/// expected, recoverable failures (e.g. invalid credentials, validation errors).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, null);
    public static Result<T> Failure<T>(string error) => new(default, false, error);
}

/// <summary>
/// A <see cref="Result"/> that also carries a value on success.
/// </summary>
public class Result<T> : Result
{
    internal Result(T? value, bool isSuccess, string? error) : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static implicit operator Result<T>(T value) => Success(value);
}
