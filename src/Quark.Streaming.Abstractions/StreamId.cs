namespace Quark.Streaming.Abstractions;

/// <summary>Identifies a stream by namespace + key. Drop-in equivalent of Orleans' <c>StreamId</c>.</summary>
public readonly struct StreamId : IEquatable<StreamId>
{
    private StreamId(string @namespace, string key) { Namespace = @namespace; Key = key; }

    public string Namespace { get; }
    public string Key { get; }

    public static StreamId Create(string @namespace, string key) => new(@namespace, key);
    public static StreamId Create(string @namespace, Guid key) => new(@namespace, key.ToString("N"));

    public bool Equals(StreamId other) => Namespace == other.Namespace && Key == other.Key;
    public override bool Equals(object? obj) => obj is StreamId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Namespace, Key);
    public override string ToString() => $"{Namespace}/{Key}";
}
