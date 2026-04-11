using Quark.Core.Abstractions;
using System.Collections.Concurrent;

namespace Quark.Runtime;

/// <summary>
/// Maps <see cref="GrainType"/> keys to concrete CLR types.
/// Registration is explicit and AOT-safe — no assembly scanning.
/// </summary>
public sealed class GrainTypeRegistry : IGrainTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _map = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a grain implementation type under its <see cref="GrainType"/> key.
    /// </summary>
    public void Register(GrainType grainType, Type grainClass)
    {
        ArgumentNullException.ThrowIfNull(grainClass);
        _map[grainType.Value] = grainClass;
    }

    /// <summary>Convenience overload: infers the grain type key from the CLR type name.</summary>
    public void Register<TGrain>() where TGrain : Grain =>
        Register(new GrainType(typeof(TGrain).Name), typeof(TGrain));

    /// <inheritdoc/>
    public bool TryGetGrainClass(GrainType grainType, out Type? grainClass) =>
        _map.TryGetValue(grainType.Value, out grainClass);

    /// <inheritdoc/>
    public IEnumerable<(GrainType GrainType, Type GrainClass)> GetAll()
    {
        foreach (var kvp in _map)
            yield return (new GrainType(kvp.Key), kvp.Value);
    }
}
