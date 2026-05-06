namespace Quark.Core.Abstractions.Identity;

/// <summary>
///     Represents the type component of a <see cref="GrainId" />.
///     Uses an interned string internally to avoid allocations on hot paths.
/// </summary>
public readonly struct GrainType : IEquatable<GrainType>, IComparable<GrainType>
{
    private readonly string _value;

    /// <summary>Creates a <see cref="GrainType" /> from a raw string value.</summary>
    public GrainType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = string.IsInterned(value) ?? value;
    }

    /// <summary>The string representation of this grain type.</summary>
    public string Value => _value ?? string.Empty;

    /// <inheritdoc />
    public bool Equals(GrainType other)
    {
        return string.Equals(_value, other._value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GrainType other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _value?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return _value ?? string.Empty;
    }

    /// <inheritdoc />
    public int CompareTo(GrainType other)
    {
        return string.Compare(_value, other._value, StringComparison.Ordinal);
    }

    /// <summary>Converts a <see cref="GrainType" /> to its string value.</summary>
    public static implicit operator string(GrainType type)
    {
        return type.Value;
    }

    /// <summary>Creates a <see cref="GrainType" /> from a string.</summary>
    public static implicit operator GrainType(string value)
    {
        return new GrainType(value);
    }

    /// <inheritdoc cref="IEquatable{T}" />
    public static bool operator ==(GrainType left, GrainType right)
    {
        return left.Equals(right);
    }

    /// <inheritdoc cref="IEquatable{T}" />
    public static bool operator !=(GrainType left, GrainType right)
    {
        return !left.Equals(right);
    }
}
