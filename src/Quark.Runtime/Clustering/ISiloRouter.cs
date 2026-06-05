using System.Diagnostics.CodeAnalysis;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime.Clustering;

/// <summary>
///     Routes grain calls to the correct silo when the target silo is not the local one.
///     Only present when multi-silo clustering is configured.
/// </summary>
public interface ISiloRouter
{
    /// <summary>Registers an invoker for the given silo address.</summary>
    void Register(SiloAddress address, IGrainCallInvoker invoker);

    /// <summary>Removes the invoker for the given silo address.</summary>
    void Unregister(SiloAddress address);

    /// <summary>
    ///     Returns the <see cref="IGrainCallInvoker" /> for the given silo, or <see langword="false" />
    ///     if the silo is not currently reachable.
    /// </summary>
    bool TryGetInvoker(SiloAddress address, [NotNullWhen(true)] out IGrainCallInvoker? invoker);
}
