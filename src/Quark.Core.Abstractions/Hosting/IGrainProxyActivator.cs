using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Implemented by every generated grain proxy. Provides a static AOT-safe factory method
///     so the proxy can be constructed without <c>Activator.CreateInstance</c> or reflection.
/// </summary>
public interface IGrainProxyActivator<TSelf>
    where TSelf : class, IGrain, IGrainProxyActivator<TSelf>
{
    /// <summary>Creates a new proxy instance bound to <paramref name="grainId" /> and <paramref name="invoker" />.</summary>
    static abstract TSelf Create(GrainId grainId, IGrainCallInvoker invoker);
}
