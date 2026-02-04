using Quark.Abstractions;
using Quark.Core.Actors.Pooling;

namespace Quark.Core.Actors;

/// <summary>
///     Base implementation of an actor message.
/// </summary>
public abstract class ActorMessageBase : IActorMessage
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorMessageBase" /> class.
    /// </summary>
    protected ActorMessageBase()
    {
        // Phase 8.1: Zero-allocation messaging - use incrementing ID instead of GUID
        MessageId = MessageIdGenerator.Generate();
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public string MessageId { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; }
}