using Quark.Streaming.Abstractions;

namespace Quark.Client.Tcp;

internal sealed class DelegateObserver<T> : IAsyncObserver<T>
{
    private readonly Func<T, StreamSequenceToken?, ValueTask> _onNext;
    private readonly Func<Exception, ValueTask>? _onError;
    private readonly Func<ValueTask>? _onCompleted;

    public DelegateObserver(
        Func<T, StreamSequenceToken?, ValueTask> onNext,
        Func<Exception, ValueTask>? onError,
        Func<ValueTask>? onCompleted)
    {
        _onNext = onNext;
        _onError = onError;
        _onCompleted = onCompleted;
    }

    public ValueTask OnNextAsync(T item, StreamSequenceToken? token = null) => _onNext(item, token);
    public ValueTask OnErrorAsync(Exception ex) => _onError?.Invoke(ex) ?? ValueTask.CompletedTask;
    public ValueTask OnCompletedAsync() => _onCompleted?.Invoke() ?? ValueTask.CompletedTask;
}

internal sealed class DelegateContextObserver<T, TContext> : IAsyncObserver<T>
{
    private readonly TContext _context;
    private readonly Func<TContext, T, StreamSequenceToken?, ValueTask> _onNext;
    private readonly Func<Exception, ValueTask>? _onError;
    private readonly Func<ValueTask>? _onCompleted;

    public DelegateContextObserver(
        TContext context,
        Func<TContext, T, StreamSequenceToken?, ValueTask> onNext,
        Func<Exception, ValueTask>? onError,
        Func<ValueTask>? onCompleted)
    {
        _context = context;
        _onNext = onNext;
        _onError = onError;
        _onCompleted = onCompleted;
    }

    public ValueTask OnNextAsync(T item, StreamSequenceToken? token = null) => _onNext(_context, item, token);
    public ValueTask OnErrorAsync(Exception ex) => _onError?.Invoke(ex) ?? ValueTask.CompletedTask;
    public ValueTask OnCompletedAsync() => _onCompleted?.Invoke() ?? ValueTask.CompletedTask;
}
