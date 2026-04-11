namespace Quark.Serialization.Abstractions;

/// <summary>
/// Marks a type for AOT-safe Quark serialization code generation.
/// The Quark source generator emits a field codec, a deep copier,
/// and (if applicable) a grain argument serializer for every type annotated with this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface,
    AllowMultiple = false,
    Inherited = false)]
public sealed class GenerateSerializerAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c> (default), the generator also emits a deep-copy helper.
    /// Set to <c>false</c> for immutable types where a copy is always a reference to the original.
    /// </summary>
    public bool GenerateCopier { get; set; } = true;
}
