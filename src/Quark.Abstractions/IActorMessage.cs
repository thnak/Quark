namespace Quark.Abstractions;

/// <summary>
/// Represents a message to be processed by an actor.
/// </summary>
public interface IActorMessage
{
    /// <summary>
    /// Gets the unique identifier for this message.
    /// </summary>
    string MessageId { get; }

    /// <summary>
    /// Gets the correlation ID for distributed tracing.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Gets the timestamp when the message was created.
    /// </summary>
    DateTimeOffset Timestamp { get; }
}

/// <summary>
/// Represents a message that invokes a method on an actor.
/// </summary>
/// <typeparam name="TResult">The result type of the method invocation.</typeparam>
public interface IActorMethodMessage<TResult> : IActorMessage
{
    /// <summary>
    /// Gets the name of the method to invoke.
    /// </summary>
    string MethodName { get; }

    /// <summary>
    /// Gets the arguments for the method invocation.
    /// </summary>
    object?[] Arguments { get; }

    /// <summary>
    /// Gets the task completion source for the result.
    /// </summary>
    TaskCompletionSource<TResult> CompletionSource { get; }
}
