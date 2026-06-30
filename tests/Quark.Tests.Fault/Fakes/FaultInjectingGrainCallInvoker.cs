using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Tests.Fault.FaultScenario;

namespace Quark.Tests.Fault.Fakes;

/// <summary>
/// Wraps IGrainCallInvoker to inject simulated call failures (dropped calls, timeouts).
/// Intercepts both external client calls and inter-grain calls routed through the same invoker.
/// </summary>
public sealed class FaultInjectingGrainCallInvoker : IGrainCallInvoker
{
    private readonly IGrainCallInvoker _inner;
    private readonly CallFaultPlan _plan;

    public FaultInjectingGrainCallInvoker(IGrainCallInvoker inner, CallFaultPlan plan)
    {
        _inner = inner;
        _plan = plan;
    }

    public ValueTask<TResult> InvokeAsync<TInvokable, TResult>(GrainId id, TInvokable invokable, CancellationToken ct = default)
        where TInvokable : struct, IGrainInvokable<TResult>
    {
        _plan.Check(id, invokable.MethodId);
        return _inner.InvokeAsync<TInvokable, TResult>(id, invokable, ct);
    }

    public ValueTask InvokeVoidAsync<TInvokable>(GrainId id, TInvokable invokable, CancellationToken ct = default)
        where TInvokable : struct, IGrainVoidInvokable
    {
        _plan.Check(id, invokable.MethodId);
        return _inner.InvokeVoidAsync<TInvokable>(id, invokable, ct);
    }

    public ValueTask InvokeObserverAsync<TInvokable>(GrainId id, TInvokable invokable, CancellationToken ct = default)
        where TInvokable : struct, IObserverVoidInvokable
        => _inner.InvokeObserverAsync<TInvokable>(id, invokable, ct);
}
