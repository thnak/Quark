// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions;

/// <summary>
/// Interface for binary converters that serialize and deserialize method parameters.
/// </summary>
public interface IQuarkBinaryConverter
{
    /// <summary>
    /// Writes a value to the binary writer.
    /// </summary>
    /// <param name="writer">The binary writer to write to.</param>
    /// <param name="value">The value to write.</param>
    void Write(BinaryWriter writer, object? value);

    /// <summary>
    /// Reads a value from the binary reader.
    /// </summary>
    /// <param name="reader">The binary reader to read from.</param>
    /// <returns>The deserialized value.</returns>
    object? Read(BinaryReader reader);
}

/// <summary>
/// Generic interface for binary converters with type safety.
/// </summary>
/// <typeparam name="T">The type to convert.</typeparam>
public interface IQuarkBinaryConverter<T> : IQuarkBinaryConverter
{
    /// <summary>
    /// Writes a value to the binary writer.
    /// </summary>
    /// <param name="writer">The binary writer to write to.</param>
    /// <param name="value">The value to write.</param>
    void Write(BinaryWriter writer, T value);

    /// <summary>
    /// Reads a value from the binary reader.
    /// </summary>
    /// <param name="reader">The binary reader to read from.</param>
    /// <returns>The deserialized value.</returns>
    new T Read(BinaryReader reader);

    // Explicit interface implementations to avoid ambiguity
    void IQuarkBinaryConverter.Write(BinaryWriter writer, object? value)
    {
        if (value is T typedValue)
        {
            Write(writer, typedValue);
        }
        else if (value is null && default(T) is null)
        {
            Write(writer, default(T)!);
        }
        else
        {
            throw new InvalidCastException($"Cannot convert {value?.GetType().Name ?? "null"} to {typeof(T).Name}");
        }
    }

    object? IQuarkBinaryConverter.Read(BinaryReader reader)
    {
        return Read(reader);
    }
}
