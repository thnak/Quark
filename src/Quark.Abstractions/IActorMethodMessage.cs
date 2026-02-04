namespace Quark.Abstractions;

/// <summary>
///     Represents a message that invokes a method on an actor.
/// </summary>
/// <typeparam name="TResult">The result type of the method invocation.</typeparam>
public interface IActorMethodMessage<TResult> : IActorMessage
{
    /// <summary>
    ///     Gets the name of the method to invoke.
    /// </summary>
    string MethodName { get; }

    /// <summary>
    ///     Gets the arguments for the method invocation.
    /// </summary>
    object?[] Arguments { get; }

    /// <summary>
    ///     Gets the task completion source for the result.
    /// </summary>
    TaskCompletionSource<TResult> CompletionSource { get; }
}