// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions;

/// <summary>
/// Specifies a binary converter to use for serializing a method parameter or return value.
/// Multiple converters can be applied to a single method with different orders.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class BinaryConverterAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryConverterAttribute"/> class.
    /// </summary>
    /// <param name="converterType">The type of the converter. Must implement <see cref="IQuarkBinaryConverter"/>.</param>
    public BinaryConverterAttribute(Type converterType)
    {
        if (!typeof(IQuarkBinaryConverter).IsAssignableFrom(converterType))
        {
            throw new ArgumentException(
                $"Converter type {converterType.Name} must implement IQuarkBinaryConverter",
                nameof(converterType));
        }

        ConverterType = converterType;
    }

    /// <summary>
    /// Gets the type of the converter.
    /// </summary>
    public Type ConverterType { get; }

    /// <summary>
    /// Gets or sets the order in which this converter should be applied.
    /// Converters with lower order values are applied first during serialization.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the parameter name this converter applies to.
    /// If null, the converter is used for the return value.
    /// </summary>
    public string? ParameterName { get; set; }
}
