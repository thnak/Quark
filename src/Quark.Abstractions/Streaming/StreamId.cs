// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Represents a unique identifier for a stream, combining a namespace and a key.
/// </summary>
public readonly struct StreamId : IEquatable<StreamId>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StreamId"/> struct.
    /// </summary>
    /// <param name="namespace">The stream namespace (e.g., "orders/processed").</param>
    /// <param name="key">The stream key within the namespace (e.g., order ID).</param>
    public StreamId(string @namespace, string key)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace cannot be null or empty.", nameof(@namespace));
        
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        
        Namespace = @namespace;
        Key = key;
    }

    /// <summary>
    /// Gets the stream namespace.
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// Gets the stream key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the full stream identifier as a string.
    /// </summary>
    public string FullId => $"{Namespace}/{Key}";

    /// <inheritdoc/>
    public bool Equals(StreamId other)
    {
        return Namespace == other.Namespace && Key == other.Key;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is StreamId other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Namespace, Key);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return FullId;
    }

    public static bool operator ==(StreamId left, StreamId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StreamId left, StreamId right)
    {
        return !left.Equals(right);
    }
}
