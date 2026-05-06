namespace Quark.Core.Abstractions.Identity;

/// <summary>
/// Stable identifier for a grain interface type, used by the messaging layer
/// to route calls to the correct proxy/handler.
/// </summary>
public readonly struct GrainInterfaceType : IEquatable<GrainInterfaceType>
{
    private readonly string _value;

    /// <summary>Creates a <see cref="GrainInterfaceType"/> from a raw string.</summary>
    public GrainInterfaceType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = string.IsInterned(value) ?? value;
    }

    /// <summary>The string representation of this interface type.</summary>
    public string Value => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(GrainInterfaceType other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GrainInterfaceType other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;

    /// <inheritdoc/>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc cref="IEquatable{T}"/>
    public static bool operator ==(GrainInterfaceType left, GrainInterfaceType right) => left.Equals(right);

    /// <inheritdoc cref="IEquatable{T}"/>
    public static bool operator !=(GrainInterfaceType left, GrainInterfaceType right) => !left.Equals(right);
}
