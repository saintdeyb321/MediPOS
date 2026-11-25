namespace MediPOS.Api.Errors;

// Fixed server-owned status/code only. Never propagate parser messages or request values.
public sealed class HttpBoundaryException(int statusCode, string code, int? retryAfterSeconds = null) : Exception("Request could not be completed.")
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
