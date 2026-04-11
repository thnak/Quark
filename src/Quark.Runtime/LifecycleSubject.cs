using Quark.Core.Abstractions.Lifecycle;

namespace Quark.Runtime;

/// <summary>
/// Concrete implementation of <see cref="ILifecycleSubject"/>.
/// Observers are called in <em>ascending</em> stage order on start and
/// <em>descending</em> stage order on stop (mirrors Orleans semantics).
/// </summary>
public sealed class LifecycleSubject : ILifecycleSubject
{
    private readonly object _lock = new();
    private readonly List<ObserverEntry> _entries = new();
    private bool _started;

    /// <inheritdoc/>
    public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observerName);
        ArgumentNullException.ThrowIfNull(observer);

        var entry = new ObserverEntry(observerName, stage, observer);
        lock (_lock)
        {
            _entries.Add(entry);
        }
        return new Unsubscriber(this, entry);
    }

    /// <summary>
    /// Starts all subscribed observers in ascending stage order.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        List<ObserverEntry> snapshot;
        lock (_lock)
        {
            if (_started) return;
            _started = true;
            snapshot = new List<ObserverEntry>(_entries);
        }

        snapshot.Sort(static (a, b) => a.Stage.CompareTo(b.Stage));
        foreach (ObserverEntry entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await entry.Observer.OnStart(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops all subscribed observers in descending stage order (reverse of start).
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        List<ObserverEntry> snapshot;
        lock (_lock)
        {
            snapshot = new List<ObserverEntry>(_entries);
        }

        snapshot.Sort(static (a, b) => b.Stage.CompareTo(a.Stage));
        var exceptions = new List<Exception>();
        foreach (ObserverEntry entry in snapshot)
        {
            try
            {
                await entry.Observer.OnStop(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count == 1) throw exceptions[0];
        if (exceptions.Count > 1) throw new AggregateException(exceptions);
    }

    private void Remove(ObserverEntry entry)
    {
        lock (_lock)
        {
            _entries.Remove(entry);
        }
    }

    private sealed class ObserverEntry(string name, int stage, ILifecycleObserver observer)
    {
        public string Name { get; } = name;
        public int Stage { get; } = stage;
        public ILifecycleObserver Observer { get; } = observer;
    }

    private sealed class Unsubscriber(LifecycleSubject subject, ObserverEntry entry) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            subject.Remove(entry);
        }
    }
}
