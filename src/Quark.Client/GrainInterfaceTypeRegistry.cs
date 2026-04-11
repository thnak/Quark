using Quark.Core.Abstractions;

namespace Quark.Client;

/// <summary>
/// Registry that maps grain interface CLR types to their <see cref="GrainType"/>.
/// Populated at startup via <c>AddGrainProxy&lt;TInterface, TProxy&gt;()</c>.
/// </summary>
public sealed class GrainInterfaceTypeRegistry
{
    private readonly Dictionary<Type, GrainType> _map = new();

    /// <summary>Registers a mapping from <paramref name="interfaceType"/> to <paramref name="grainType"/>.</summary>
    public void Register(Type interfaceType, GrainType grainType)
    {
        _map[interfaceType] = grainType;
    }

    /// <summary>
    /// Returns the <see cref="GrainType"/> associated with the grain interface CLR type,
    /// or throws if none is registered.
    /// </summary>
    public GrainType GetGrainType(Type interfaceType)
    {
        if (_map.TryGetValue(interfaceType, out var gt)) return gt;

        throw new InvalidOperationException(
            $"No GrainType registered for interface '{interfaceType.FullName}'. " +
            "Call services.AddGrainProxy<TInterface, TProxy>() during startup.");
    }
}
