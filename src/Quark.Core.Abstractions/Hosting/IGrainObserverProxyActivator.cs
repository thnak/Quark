using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     AOT-safe static factory contract for observer proxy classes.
///     The generated proxy for <c>IMyObserver</c> implements
///     <c>IGrainObserverProxyActivator&lt;MyObserverProxy&gt;</c> so the proxy can be
///     constructed without reflection.
/// </summary>
public interface IGrainObserverProxyActivator<TSelf>
    where TSelf : class, IGrainObserver, IGrainObserverProxyActivator<TSelf>
{
    /// <summary>Creates a proxy instance for the given grain identity and invoker.</summary>
    static abstract TSelf Create(GrainId grainId, IGrainCallInvoker invoker);
}
