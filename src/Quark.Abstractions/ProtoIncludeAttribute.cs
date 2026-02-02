using System;

namespace Quark.Abstractions;

/// <summary>
/// Attribute to explicitly include a type in the ProtoSerializer context.
/// Use this to register types that should be serializable via ProtoBuf.
/// </summary>
/// <example>
/// <code>
/// [ProtoSerializerContext]
/// [ProtoInclude(typeof(OrderState))]
/// [ProtoInclude(typeof(CreateOrderRequest))]
/// public partial class PizzaSerializerContext : IProtoSerializerContext
/// {
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ProtoIncludeAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoIncludeAttribute"/> class.
    /// </summary>
    /// <param name="type">The type to include in the serializer context.</param>
    public ProtoIncludeAttribute(Type type)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
    }

    /// <summary>
    /// Gets the type to include in the serializer context.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets or sets a custom converter type for this type.
    /// The converter must implement IProtoConverter&lt;T&gt;.
    /// </summary>
    public Type? ConverterType { get; set; }
}
