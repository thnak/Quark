namespace Quark.Serialization.Abstractions.Buffers;

/// <summary>Sub-type for <see cref="WireType.Extended"/> fields.</summary>
public enum ExtendedWireType : byte
{
    /// <summary>Null reference.</summary>
    Null = 0,

    /// <summary>The value's type matches the declared/expected type exactly.</summary>
    ExpectedType = 1,

    /// <summary>The value's type differs from the declared type (polymorphic).</summary>
    PolymorphicType = 2,

    /// <summary>The field holds its default (zero/empty) value.</summary>
    DefaultValue = 3,
}