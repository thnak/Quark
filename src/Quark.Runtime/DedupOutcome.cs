namespace Quark.Runtime;

/// <summary>Outcome returned by <see cref="IRequestDedupStore.TryBeginAsync" />.</summary>
public enum DedupOutcome
{
    /// <summary>No prior entry; the caller must execute the method and call <see cref="IRequestDedupStore.Complete" />.</summary>
    Execute,

    /// <summary>A completed entry exists; the caller must return <see cref="DedupLease.RecordedResponse" /> without re-executing.</summary>
    Replay,

    /// <summary>A prior entry exists but with a different argument hash (key reuse with different args); the call must be rejected.</summary>
    Conflict,
}