using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Routes an outbound grain call from a grain proxy to the runtime dispatcher.
///     All overloads are strongly-typed — no argument boxing or array allocation.
///     Implemented by the runtime and injected into generated proxy classes.
/// </summary>
public interface IGrainCallInvoker
{
    /// <summary>
    ///     AOT-safe typed overload: invokes a grain method that returns <see cref="Task{TResult}" />
    ///     or <see cref="ValueTask{TResult}" /> via a strongly-typed invokable struct.
    /// </summary>
    Task<TResult> InvokeAsync<TInvokable, TResult>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainInvokable<TResult>;

    /// <summary>
    ///     AOT-safe typed overload: invokes a grain method that returns <see cref="Task" />
    ///     or <see cref="ValueTask" /> via a strongly-typed invokable struct.
    /// </summary>
    Task InvokeVoidAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainVoidInvokable;

    /// <summary>
    ///     AOT-safe typed overload: invokes a method on a local observer object via a strongly-typed
    ///     invokable struct.  The runtime looks up the observer's target CLR object by
    ///     <paramref name="grainId" /> and calls <c>invokable.Invoke(target)</c> directly.
    /// </summary>
    Task InvokeObserverAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IObserverVoidInvokable;
}
