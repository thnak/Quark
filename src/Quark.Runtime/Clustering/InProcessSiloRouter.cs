using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime.Clustering;

/// <summary>
///     In-process implementation of <see cref="ISiloRouter" />.
///     Maintains a map of <see cref="SiloAddress" /> → <see cref="IGrainCallInvoker" />
///     for all silos sharing the same process (localhost clustering, test clusters).
/// </summary>
public sealed class InProcessSiloRouter : ISiloRouter
{
    private readonly ConcurrentDictionary<SiloAddress, IGrainCallInvoker> _invokers = new();

    /// <inheritdoc />
    public void Register(SiloAddress address, IGrainCallInvoker invoker)
        => _invokers[address] = invoker;

    /// <inheritdoc />
    public void Unregister(SiloAddress address)
        => _invokers.TryRemove(address, out _);

    /// <inheritdoc />
    public bool TryGetInvoker(SiloAddress address, [NotNullWhen(true)] out IGrainCallInvoker? invoker)
        => _invokers.TryGetValue(address, out invoker);
}
