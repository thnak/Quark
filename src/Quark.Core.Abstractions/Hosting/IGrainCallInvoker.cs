using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Routes an outbound grain call from a grain proxy to the runtime dispatcher.
///     Implemented by the runtime and injected into generated proxy classes.
/// </summary>
public interface IGrainCallInvoker
{
    /// <summary>
    ///     Invokes a grain method and returns the raw boxed result.
    ///     This is used by the runtime dispatcher for network-routed calls where the result type is
    ///     not known at compile time.
    /// </summary>
    Task<object?> InvokeAsync(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Invokes a grain method that returns <see cref="Task{TResult}" />.
    /// </summary>
    /// <typeparam name="TResult">The return type of the grain method.</typeparam>
    /// <param name="grainId">Target grain identity.</param>
    /// <param name="methodId">Stable numeric method identifier (assigned by the codegen).</param>
    /// <param name="arguments">Serialized or boxed method arguments.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<TResult> InvokeAsync<TResult>(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Invokes a grain method that returns <see cref="Task" /> (void-like).
    /// </summary>
    Task InvokeVoidAsync(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     AOT-safe typed overload: invokes a grain method that returns <see cref="Task{TResult}" />
    ///     or <see cref="ValueTask{TResult}" /> via a strongly-typed invokable struct.
    ///     No argument boxing, no array allocation.
    /// </summary>
    Task<TResult> InvokeAsync<TInvokable, TResult>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainInvokable<TResult>;

    /// <summary>
    ///     AOT-safe typed overload: invokes a grain method that returns <see cref="Task" />
    ///     or <see cref="ValueTask" /> via a strongly-typed invokable struct.
    ///     No argument boxing, no array allocation.
    /// </summary>
    Task InvokeVoidAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainVoidInvokable;

    /// <summary>
    ///     AOT-safe typed overload: invokes a method on a local observer object via a strongly-typed
    ///     invokable struct.  The runtime looks up the observer's target CLR object by
    ///     <paramref name="grainId" /> and calls <c>invokable.Invoke(target)</c> directly — no
    ///     boxing, no method-ID fan-in.
    /// </summary>
    Task InvokeObserverAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IObserverVoidInvokable;
}
