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
        => other is SequentialToken st ? SequenceNumber.CompareTo(st.SequenceNumber) : 1;

    public override bool Newer(StreamSequenceToken other)
        => other is SequentialToken st && SequenceNumber > st.SequenceNumber;
}
