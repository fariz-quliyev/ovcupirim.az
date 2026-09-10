namespace Ovcuprim.Application.Common;

public enum ResultError
{
    None = 0,
    NotFound = 1,
    Validation = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,
    RateLimited = 6,

    /// <summary>
    /// Could not be decided right now — an upstream dependency was unreachable or has not reached a
    /// final state — and should be retried later. Nothing was changed. Controllers answer 503.
    /// </summary>
    Unavailable = 7
}

/// <summary>Outcome of an application operation. Controllers translate this into a status code.</summary>
public class Result
{
    protected Result(bool succeeded, ResultError error, string? message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        Succeeded = succeeded;
        Error = error;
        Message = message;
        FieldErrors = fieldErrors;
    }

    public bool Succeeded { get; }

    public ResultError Error { get; }

    public string? Message { get; }

    /// <summary>
    /// Per-field messages, keyed the same way FluentValidation keys them, so a service-level
    /// failure lands on the right form field through the one ProblemDetails contract.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    public static Result Success() => new(true, ResultError.None, null);

    public static Result Failure(ResultError error, string message) => new(false, error, message);

    public static Result NotFound(string message = "Resource not found.") => Failure(ResultError.NotFound, message);

    public static Result Conflict(string message) => Failure(ResultError.Conflict, message);

    public static Result Forbidden(string message = "Not allowed.") => Failure(ResultError.Forbidden, message);

    public static Result Unavailable(string message) => Failure(ResultError.Unavailable, message);

    public static Result Invalid(IReadOnlyDictionary<string, string[]> fieldErrors, string message = "Məlumatlar düzgün deyil.") =>
        new(false, ResultError.Validation, message, fieldErrors);

    public static Result Invalid(string field, string message) =>
        Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] }, message);
}

public sealed class Result<T> : Result
{
    private Result(bool succeeded, T? value, ResultError error, string? message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        : base(succeeded, error, message, fieldErrors) => Value = value;

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, ResultError.None, null);

    public static new Result<T> Failure(ResultError error, string message) => new(false, default, error, message);

    public static new Result<T> NotFound(string message = "Resource not found.") => Failure(ResultError.NotFound, message);

    public static new Result<T> Conflict(string message) => Failure(ResultError.Conflict, message);

    public static new Result<T> Forbidden(string message = "Not allowed.") => Failure(ResultError.Forbidden, message);

    public static new Result<T> Invalid(IReadOnlyDictionary<string, string[]> fieldErrors, string message = "Məlumatlar düzgün deyil.") =>
        new(false, default, ResultError.Validation, message, fieldErrors);

    public static new Result<T> Invalid(string field, string message) =>
        Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] }, message);

    /// <summary>Carries a non-generic failure (including its field errors) into a typed result.</summary>
    public static Result<T> From(Result result) =>
        new(false, default, result.Error, result.Message, result.FieldErrors);
}
