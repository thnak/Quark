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

    public Task<object?> InvokeAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
    {
        _plan.Check(id, method);
        return _inner.InvokeAsync(id, method, args, ct);
    }

    public Task<TResult> InvokeAsync<TResult>(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
    {
        _plan.Check(id, method);
        return _inner.InvokeAsync<TResult>(id, method, args, ct);
    }

    public Task InvokeVoidAsync(GrainId id, uint method, object?[]? args = null, CancellationToken ct = default)
    {
        _plan.Check(id, method);
        return _inner.InvokeVoidAsync(id, method, args, ct);
    }
}
