namespace Quark.Serialization.Abstractions.Buffers;

/// <summary>
///     Wire type used in the binary field header, indicating how many bytes follow the tag.
///     Mirrors the encoding used by the Orleans serializer for future binary compatibility.
/// </summary>
public enum WireType : byte
{
    /// <summary>Variable-length integer (LEB128).</summary>
    VarInt = 0,

    /// <summary>Tag-delimited composite (nested object), terminated by <see cref="EndTagDelimited" />.</summary>
    TagDelimited = 1,

    /// <summary>Length-prefixed bytes (string, byte array, embedded message).</summary>
    LengthPrefixed = 2,

    /// <summary>Fixed 32-bit value (float, fixed32, sfixed32).</summary>
    Fixed32 = 3,

    /// <summary>Fixed 64-bit value (double, fixed64, sfixed64).</summary>
    Fixed64 = 4,

    /// <summary>Object reference (back-reference to already-serialized object).</summary>
    Reference = 5,

    /// <summary>Extended encoding (null, default, schema version header).</summary>
    Extended = 6,

    /// <summary>Terminates a <see cref="TagDelimited" /> composite.</summary>
    EndTagDelimited = 7
}
