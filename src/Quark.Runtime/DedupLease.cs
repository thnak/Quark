namespace Quark.Runtime;

/// <summary>Result of <see cref="IRequestDedupStore.TryBeginAsync" />.</summary>
public readonly struct DedupLease
{
    public DedupLease(DedupOutcome outcome, ReadOnlyMemory<byte> recordedResponse)
    {
        Outcome = outcome;
        RecordedResponse = recordedResponse;
    }

    /// <summary>What the caller should do.</summary>
    public DedupOutcome Outcome { get; }

    /// <summary>The originally serialized response bytes. Valid only when <see cref="Outcome" /> is <see cref="DedupOutcome.Replay" />.</summary>
    public ReadOnlyMemory<byte> RecordedResponse { get; }
}