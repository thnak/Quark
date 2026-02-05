// Copyright (c) Quark Framework. All rights reserved.

using System.IO;

namespace Quark.Abstractions;

/// <summary>
/// Helper class for safe binary serialization with automatic length-prefixing.
/// Ensures each parameter's data is isolated and converters can't read beyond their boundaries.
/// </summary>
public static class BinaryConverterHelper
{
    /// <summary>
    /// Writes a value using a converter with automatic length-prefixing.
    /// The length of the serialized data is written first, followed by the data itself.
    /// This ensures safe deserialization even with variable-length types.
    /// </summary>
    /// <typeparam name="T">The type of value to write.</typeparam>
    /// <param name="writer">The binary writer to write to.</param>
    /// <param name="converter">The converter to use for serialization.</param>
    /// <param name="value">The value to serialize.</param>
    public static void WriteWithLength<T>(BinaryWriter writer, IQuarkBinaryConverter<T> converter, T value)
    {
        // Serialize to a temporary buffer to get the length
        using (var tempStream = new MemoryStream())
        {
            using (var tempWriter = new BinaryWriter(tempStream))
            {
                converter.Write(tempWriter, value);
            }
            
            var data = tempStream.ToArray();
            
            // Write the length first (as Int32)
            writer.Write(data.Length);
            
            // Then write the actual data
            writer.Write(data);
        }
    }

    /// <summary>
    /// Reads a value using a converter with automatic length-prefixing.
    /// Reads the length first, then reads exactly that many bytes before deserializing.
    /// This prevents converters from reading beyond their allocated segment.
    /// </summary>
    /// <typeparam name="T">The type of value to read.</typeparam>
    /// <param name="reader">The binary reader to read from.</param>
    /// <param name="converter">The converter to use for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    public static T ReadWithLength<T>(BinaryReader reader, IQuarkBinaryConverter<T> converter)
    {
        // Read the length of this parameter's data
        var length = reader.ReadInt32();
        
        if (length < 0)
        {
            throw new InvalidOperationException($"Invalid data length: {length}. Data may be corrupted.");
        }
        
        // Read exactly that many bytes
        var data = reader.ReadBytes(length);
        
        if (data.Length != length)
        {
            throw new InvalidOperationException(
                $"Expected to read {length} bytes but only got {data.Length}. Stream may be truncated.");
        }
        
        // Deserialize from the isolated segment
        using (var segmentStream = new MemoryStream(data))
        {
            using (var segmentReader = new BinaryReader(segmentStream))
            {
                var result = converter.Read(segmentReader);
                
                // Verify the converter consumed all the data in its segment
                if (segmentStream.Position != length)
                {
                    throw new InvalidOperationException(
                        $"Converter for type {typeof(T).Name} read {segmentStream.Position} bytes " +
                        $"but segment contains {length} bytes. Converter may be incorrect.");
                }
                
                return result;
            }
        }
    }

    /// <summary>
    /// Non-generic version for use with type-erased converters.
    /// Writes a value using a converter with automatic length-prefixing.
    /// </summary>
    /// <param name="writer">The binary writer to write to.</param>
    /// <param name="converter">The converter to use for serialization.</param>
    /// <param name="value">The value to serialize.</param>
    public static void WriteWithLength(BinaryWriter writer, IQuarkBinaryConverter converter, object? value)
    {
        // Serialize to a temporary buffer to get the length
        using (var tempStream = new MemoryStream())
        {
            using (var tempWriter = new BinaryWriter(tempStream))
            {
                converter.Write(tempWriter, value);
            }
            
            var data = tempStream.ToArray();
            
            // Write the length first (as Int32)
            writer.Write(data.Length);
            
            // Then write the actual data
            writer.Write(data);
        }
    }

    /// <summary>
    /// Non-generic version for use with type-erased converters.
    /// Reads a value using a converter with automatic length-prefixing.
    /// </summary>
    /// <param name="reader">The binary reader to read from.</param>
    /// <param name="converter">The converter to use for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    public static object? ReadWithLength(BinaryReader reader, IQuarkBinaryConverter converter)
    {
        // Read the length of this parameter's data
        var length = reader.ReadInt32();
        
        if (length < 0)
        {
            throw new InvalidOperationException($"Invalid data length: {length}. Data may be corrupted.");
        }
        
        // Read exactly that many bytes
        var data = reader.ReadBytes(length);
        
        if (data.Length != length)
        {
            throw new InvalidOperationException(
                $"Expected to read {length} bytes but only got {data.Length}. Stream may be truncated.");
        }
        
        // Deserialize from the isolated segment
        using (var segmentStream = new MemoryStream(data))
        {
            using (var segmentReader = new BinaryReader(segmentStream))
            {
                var result = converter.Read(segmentReader);
                
                // Verify the converter consumed all the data in its segment
                if (segmentStream.Position != length)
                {
                    throw new InvalidOperationException(
                        $"Converter read {segmentStream.Position} bytes " +
                        $"but segment contains {length} bytes. Converter may be incorrect.");
                }
                
                return result;
            }
        }
    }
}
