namespace Quark.Serialization.Abstractions.Attributes;

/// <summary>
/// Assigns a stable numeric identifier to a field or property that participates in serialization.
/// The id must be unique within a type and must not change once published — it forms part of
/// the binary compatibility contract.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class IdAttribute : Attribute
{
    /// <summary>The stable field id.</summary>
    public uint Id { get; }

    /// <summary>Creates an <see cref="IdAttribute"/> with the given <paramref name="id"/>.</summary>
    public IdAttribute(uint id)
    {
        Id = id;
    }
}
