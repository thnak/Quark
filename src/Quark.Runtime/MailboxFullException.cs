namespace Quark.Runtime;

/// <summary>
///     Thrown by <see cref="GrainActivation.PostAsync"/> when a bounded mailbox configured with
///     <see cref="MailboxFullMode.RejectWhenFull"/> is at capacity. Signals that the target grain is
///     overloaded so the caller can fail fast instead of queueing unbounded work.
/// </summary>
public sealed class MailboxFullException : Exception
{
    /// <summary>Initialises the exception for the given <paramref name="grainId"/> and <paramref name="capacity"/>.</summary>
    public MailboxFullException(GrainId grainId, int capacity)
        : base($"Mailbox for grain '{grainId}' is full (capacity {capacity}); the grain is overloaded.")
    {
        GrainId = grainId;
        Capacity = capacity;
    }

    /// <summary>The grain whose mailbox rejected the post.</summary>
    public GrainId GrainId { get; }

    /// <summary>The configured mailbox capacity.</summary>
    public int Capacity { get; }
}
