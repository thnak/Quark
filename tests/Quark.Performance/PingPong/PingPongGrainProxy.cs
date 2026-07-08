using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Performance.PingPong;

public sealed class PingPongGrainProxy : IPingPongGrain, IGrainProxyActivator<PingPongGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public PingPongGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static PingPongGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public ValueTask PingAsync()
        => _invoker.InvokeVoidAsync(_grainId, new PingPongBehavior_PingInvokable());
}
