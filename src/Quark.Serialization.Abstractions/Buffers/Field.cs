namespace Quark.Serialization.Abstractions;

/// <summary>
/// Describes a single field header read from the wire.
/// </summary>
public readonly struct Field
{
    /// <summary>Field identifier (matches the <c>[Id(N)]</c> attribute on the property/field).</summary>
    public uint FieldId { get; init; }

    /// <summary>The wire encoding of this field.</summary>
    public WireType WireType { get; init; }

    /// <summary>
    /// For <see cref="WireType.Extended"/> fields: the sub-type of the extension.
    /// </summary>
    public ExtendedWireType ExtendedWireType { get; init; }

    /// <summary>Whether this field is an end-of-object marker.</summary>
    public bool IsEndObject => WireType == WireType.EndTagDelimited;

    /// <summary>Whether this field represents a default/null value.</summary>
    public bool HasExpectedType => ExtendedWireType == ExtendedWireType.ExpectedType;
}

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
