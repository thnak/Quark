using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Client;

/// <summary>
///     Lightweight <see cref="IGrainCallInvoker" /> used on the TCP client side when dispatching
///     incoming <see cref="Quark.Transport.Abstractions.MessageType.ObserverInvoke" /> frames.
///     Only <see cref="InvokeObserverAsync{TInvokable}" /> is meaningful; the grain-call overloads
///     throw — they are never called in this context.
/// </summary>
public sealed class LocalObserverCallInvoker : IGrainCallInvoker
{
    private readonly ObserverRegistry _registry;

    public LocalObserverCallInvoker(ObserverRegistry registry) => _registry = registry;

    public Task<TResult> InvokeAsync<TInvokable, TResult>(
        GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainInvokable<TResult>
        => throw new NotSupportedException("Grain invocations are not supported in observer dispatch context.");

    public Task InvokeVoidAsync<TInvokable>(
        GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainVoidInvokable
        => throw new NotSupportedException("Grain invocations are not supported in observer dispatch context.");

    public Task InvokeObserverAsync<TInvokable>(
        GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
        where TInvokable : struct, IObserverVoidInvokable
    {
        if (!_registry.TryGet(grainId, out ObserverRegistry.ObserverEntry entry))
            throw new InvalidOperationException($"Observer '{grainId}' not found in local registry.");
        return invokable.Invoke(entry.Target).AsTask();
    }
}
