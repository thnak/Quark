using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

internal sealed class DelegateObserver<T> : IAsyncObserver<T>
{
    private readonly Func<T, StreamSequenceToken?, Task> _onNext;
    private readonly Func<Exception, Task>? _onError;
    private readonly Func<Task>? _onCompleted;

    public DelegateObserver(
        Func<T, StreamSequenceToken?, Task> onNext,
        Func<Exception, Task>? onError,
        Func<Task>? onCompleted)
    {
        _onNext = onNext;
        _onError = onError;
        _onCompleted = onCompleted;
    }

    public Task OnNextAsync(T item, StreamSequenceToken? token = null) => _onNext(item, token);
    public Task OnErrorAsync(Exception ex) => _onError?.Invoke(ex) ?? Task.CompletedTask;
    public Task OnCompletedAsync() => _onCompleted?.Invoke() ?? Task.CompletedTask;
}