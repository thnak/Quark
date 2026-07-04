namespace Quark.Streaming.Abstractions;

/// <summary>Represents a position in a stream for ordered delivery.</summary>
public abstract class StreamSequenceToken : IComparable<StreamSequenceToken>
{
    public abstract int CompareTo(StreamSequenceToken? other);
    public abstract bool Newer(StreamSequenceToken other);// TODO did not implemented or used in any elsewhere
}
