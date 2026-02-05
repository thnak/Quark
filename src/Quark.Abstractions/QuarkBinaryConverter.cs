// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions;

/// <summary>
/// Abstract base class for binary converters.
/// </summary>
/// <typeparam name="T">The type to convert.</typeparam>
public abstract class QuarkBinaryConverter<T> : IQuarkBinaryConverter<T>
{
    /// <summary>
    /// Writes a value to the binary writer.
    /// </summary>
    /// <param name="writer">The binary writer to write to.</param>
    /// <param name="value">The value to write.</param>
    public abstract void Write(BinaryWriter writer, T value);

    /// <summary>
    /// Reads a value from the binary reader.
    /// </summary>
    /// <param name="reader">The binary reader to read from.</param>
    /// <returns>The deserialized value.</returns>
    public abstract T Read(BinaryReader reader);
}
