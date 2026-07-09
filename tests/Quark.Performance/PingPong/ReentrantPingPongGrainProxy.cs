using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Performance.PingPong;

public sealed class ReentrantPingPongGrainProxy : IReentrantPingPongGrain, IGrainProxyActivator<ReentrantPingPongGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public ReentrantPingPongGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static ReentrantPingPongGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public ValueTask PingAsync()
        => _invoker.InvokeVoidAsync(_grainId, new ReentrantPingPongBehavior_PingInvokable());
}
