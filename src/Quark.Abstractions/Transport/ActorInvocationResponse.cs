namespace Quark.Abstractions.Transport;

/// <summary>
///     Represents the response from a remote actor invocation.
/// </summary>
public sealed class ActorInvocationResponse
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorInvocationResponse" /> class for a successful invocation.
    /// </summary>
    public ActorInvocationResponse(string requestId, object? result)
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Result = result;
        IsSuccess = true;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorInvocationResponse" /> class for a failed invocation.
    /// </summary>
    public ActorInvocationResponse(string requestId, Exception exception)
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        IsSuccess = false;
    }

    /// <summary>
    ///     Gets the request ID this response corresponds to.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    ///     Gets whether the invocation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    ///     Gets the result of the invocation (null if failed).
    /// </summary>
    public object? Result { get; }

    /// <summary>
    ///     Gets the exception if the invocation failed (null if successful).
    /// </summary>
    public Exception? Exception { get; }
}