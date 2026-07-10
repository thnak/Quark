using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.ActorLifecycle;

/// <summary>Minimal grain whose sole purpose is to make real activation/deactivation hooks fire.</summary>
public interface IActorLifecycleGrain : IGrainWithStringKey
{
    ValueTask PingAsync();
}
