namespace Quark.Serialization.Abstractions;

/// <summary>
/// Provides an alternative short name for a type used during serialization.
/// Useful for cross-language compatibility or renaming types without breaking
/// existing serialized data.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, AllowMultiple = true, Inherited = false)]
public sealed class AliasAttribute : Attribute
{
    /// <summary>The alias name.</summary>
    public string Alias { get; }

    /// <summary>Creates an <see cref="AliasAttribute"/> with the given <paramref name="alias"/>.</summary>
    public AliasAttribute(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        Alias = alias;
    }
}
