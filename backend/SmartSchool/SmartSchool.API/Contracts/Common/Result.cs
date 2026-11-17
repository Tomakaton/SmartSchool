namespace SmartSchool.API.Contracts.Common;


/// <summary>Outcome of an operation that has no value to return.</summary>
/// <param name="Succeeded">Whether the operation completed successfully.</param>
/// <param name="Error">User-facing reason for the failure, or <c>null</c> on success.</param>
public sealed record Result(bool Succeeded, string? Error)
{
    public static Result Success()
        => new(true, null);

    public static Result Failure(string error)
        => new(false, error);
}

/// <summary>Outcome of an operation that returns a value when successful.</summary>
/// <param name="Succeeded">Whether the operation completed successfully.</param>
/// <param name="Value">The produced value, or <c>default</c> on failure.</param>
/// <param name="Error">User-facing reason for the failure, or <c>null</c> on success.</param>
public sealed record Result<T>(
    bool Succeeded,
    T? Value,
    string? Error)
{
    public static Result<T> Success(T value)
        => new(true, value, null);

    public static Result<T> Failure(string error)
        => new(false, default, error);
}
