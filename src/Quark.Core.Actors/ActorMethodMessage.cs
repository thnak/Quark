using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
///     Represents a message that invokes a method on an actor.
/// </summary>
/// <typeparam name="TResult">The result type of the method invocation.</typeparam>
public sealed class ActorMethodMessage<TResult> : ActorMessageBase, IActorMethodMessage<TResult>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorMethodMessage{TResult}" /> class.
    /// </summary>
    /// <param name="methodName">The name of the method to invoke.</param>
    /// <param name="arguments">The arguments for the method invocation.</param>
    public ActorMethodMessage(string methodName, params object?[] arguments)
    {
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        Arguments = arguments ?? Array.Empty<object?>();
        CompletionSource = new TaskCompletionSource<TResult>();
    }

    /// <inheritdoc />
    public string MethodName { get; }

    /// <inheritdoc />
    public object?[] Arguments { get; }

    /// <inheritdoc />
    public TaskCompletionSource<TResult> CompletionSource { get; }
}