namespace Quark.Core.Abstractions;

/// <summary>
///     Marks a type as immutable for data-isolation purposes.
///     The grain proxy generator will not emit a defensive deep-copy when this type
///     is used as a grain method argument or return value.
///     Apply to types whose instances are safe to share across grain call boundaries
///     without risk of mutation — e.g. sealed records, value objects, or read-only wrappers.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class ImmutableAttribute : Attribute { }
