namespace Quark.Abstractions.Transport;

/// <summary>
/// Represents a request to invoke a method on a remote actor.
/// </summary>
public sealed class ActorInvocationRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorInvocationRequest"/> class.
    /// </summary>
    public ActorInvocationRequest(
        string actorId,
        string actorType,
        string methodName,
        object?[] arguments,
        string? correlationId = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        Arguments = arguments ?? Array.Empty<object?>();
        CorrelationId = correlationId ?? Guid.NewGuid().ToString();
        RequestId = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// Gets the actor type name.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    /// Gets the method name to invoke.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Gets the method arguments.
    /// </summary>
    public object?[] Arguments { get; }

    /// <summary>
    /// Gets the correlation ID for distributed tracing.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Gets the request ID.
    /// </summary>
    public string RequestId { get; }
}

/// <summary>
/// Represents the response from a remote actor invocation.
/// </summary>
public sealed class ActorInvocationResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorInvocationResponse"/> class for a successful invocation.
    /// </summary>
    public ActorInvocationResponse(string requestId, object? result)
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Result = result;
        IsSuccess = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorInvocationResponse"/> class for a failed invocation.
    /// </summary>
    public ActorInvocationResponse(string requestId, Exception exception)
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        IsSuccess = false;
    }

    /// <summary>
    /// Gets the request ID this response corresponds to.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// Gets whether the invocation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the result of the invocation (null if failed).
    /// </summary>
    public object? Result { get; }

    /// <summary>
    /// Gets the exception if the invocation failed (null if successful).
    /// </summary>
    public Exception? Exception { get; }
}
