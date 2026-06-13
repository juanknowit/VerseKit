namespace VerseKit.PluginSdk;

/// <summary>
/// Discriminated union for fallible operations across the plugin/host boundary.
/// Avoids exception-as-control-flow while keeping error context.
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }

    private Result(T value) { IsSuccess = true; Value = value; }
    private Result(string error, Exception? ex) { IsSuccess = false; ErrorMessage = error; Exception = ex; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error, Exception? ex = null) => new(error, ex);

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsSuccess ? Result<TOut>.Success(mapper(Value!)) : Result<TOut>.Failure(ErrorMessage!, Exception);
}
