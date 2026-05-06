namespace Quark.Core.Abstractions.Identity;

/// <summary>
///     The stable, cluster-wide identity of a grain instance.
///     A <see cref="GrainId" /> is composed of a <see cref="GrainType" /> (what kind of grain)
///     and a string key (which instance of that kind).
/// </summary>
public readonly struct GrainId : IEquatable<GrainId>, IComparable<GrainId>
{
    /// <summary>Creates a <see cref="GrainId" /> from a type and a string key.</summary>
    public GrainId(GrainType type, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Type = type;
        Key = key;
    }

    /// <summary>The grain type.</summary>
    public GrainType Type { get; }

    /// <summary>The grain key.</summary>
    public string Key { get; }

    /// <summary>Creates a grain id from an interface type and a string key.</summary>
    public static GrainId Create(GrainType type, string key)
    {
        return new GrainId(type, key);
    }

    /// <summary>Creates a grain id from an interface type and a <see cref="Guid" /> key.</summary>
    public static GrainId Create(GrainType type, Guid key)
    {
        return new GrainId(type, key.ToString("N"));
    }

    /// <summary>Creates a grain id from an interface type and an integer key.</summary>
    public static GrainId Create(GrainType type, long key)
    {
        return new GrainId(type, key.ToString());
    }

    /// <inheritdoc />
    public bool Equals(GrainId other)
    {
        return Type == other.Type &&
               string.Equals(Key, other.Key, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GrainId other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Key);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Type}/{Key}";
    }

    /// <inheritdoc />
    public int CompareTo(GrainId other)
    {
        int typeComp = Type.CompareTo(other.Type);
        return typeComp != 0 ? typeComp : string.Compare(Key, other.Key, StringComparison.Ordinal);
    }

    /// <inheritdoc cref="IEquatable{T}" />
    public static bool operator ==(GrainId left, GrainId right)
    {
        return left.Equals(right);
    }

    /// <inheritdoc cref="IEquatable{T}" />
    public static bool operator !=(GrainId left, GrainId right)
    {
        return !left.Equals(right);
    }
}
