namespace Quark.Serialization.Abstractions.Buffers;

/// <summary>Sub-type for <see cref="WireType.Extended" /> fields.</summary>
public enum ExtendedWireType : byte
{
    /// <summary>Null reference.</summary>
    Null = 0,

    /// <summary>The value's type matches the declared/expected type exactly.</summary>
    ExpectedType = 1
}
