using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired each time a work item is enqueued in a grain's mailbox.</summary>
public readonly struct MailboxEnqueuedEvent(GrainId grainId, int pendingCount)
{
    public GrainId GrainId { get; } = grainId;
    /// <summary>Number of work items pending in the mailbox after this enqueue.</summary>
    public int PendingCount { get; } = pendingCount;
}