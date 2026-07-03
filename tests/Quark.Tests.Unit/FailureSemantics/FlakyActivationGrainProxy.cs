using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.FailureSemantics;

public sealed class FlakyActivationGrainProxy : IFlakyActivationGrain, IGrainProxyActivator<FlakyActivationGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public FlakyActivationGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static FlakyActivationGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task<int> PingAsync()
        => _invoker.InvokeAsync<FlakyActivationGrain_PingInvokable, int>(_grainId, new FlakyActivationGrain_PingInvokable()).AsTask();
}
