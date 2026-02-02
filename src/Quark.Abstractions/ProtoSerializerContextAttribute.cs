using System;

namespace Quark.Abstractions;

/// <summary>
/// Attribute to mark a class as a ProtoSerializer context that defines types for ProtoBuf serialization.
/// Similar to JsonSerializerContext, this allows users to register custom types and converters
/// for serialization without modifying the Quark framework code.
/// </summary>
/// <example>
/// <code>
/// [ProtoSerializerContext]
/// [ProtoInclude(typeof(MyCustomType))]
/// [ProtoInclude(typeof(AnotherType))]
/// public partial class MySerializerContext : IProtoSerializerContext
/// {
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProtoSerializerContextAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of the context.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to automatically register all types
    /// found in the assembly with [ProtoContract] attribute.
    /// </summary>
    public bool AutoRegisterProtoContracts { get; set; } = true;
}
