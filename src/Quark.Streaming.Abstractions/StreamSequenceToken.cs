namespace Quark.Streaming.Abstractions;

/// <summary>Represents a position in a stream for ordered delivery.</summary>
public abstract class StreamSequenceToken : IComparable<StreamSequenceToken>
{
    public abstract int CompareTo(StreamSequenceToken? other);
    public abstract bool Newer(StreamSequenceToken other);
}

/// <summary>Simple sequential-integer token used by the in-memory provider.</summary>
public sealed class SequentialToken : StreamSequenceToken
{
    public SequentialToken(long sequenceNumber) => SequenceNumber = sequenceNumber;
    public long SequenceNumber { get; }

    public override int CompareTo(StreamSequenceToken? other)
    {
        if (other is null) return 1;
        if (other is SequentialToken st) return SequenceNumber.CompareTo(st.SequenceNumber);
        throw new ArgumentException($"Cannot compare SequentialToken to {other.GetType().Name}.", nameof(other));
    }

    public override bool Newer(StreamSequenceToken other)
        => other is SequentialToken st && SequenceNumber > st.SequenceNumber;
}
