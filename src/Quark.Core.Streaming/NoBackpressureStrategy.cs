// Copyright (c) Quark Framework. All rights reserved.

using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// No-op backpressure strategy that passes messages through immediately.
/// Phase 8.5: Default strategy for backward compatibility.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal sealed class NoBackpressureStrategy<T> : IStreamBackpressureStrategy<T>
{
    private readonly Queue<T> _buffer = new();

    public NoBackpressureStrategy()
    {
        Metrics = new StreamBackpressureMetrics();
    }

    /// <inheritdoc/>
    public StreamBackpressureMetrics Metrics { get; }

    /// <inheritdoc/>
    public Task<bool> TryPublishAsync(T message, CancellationToken cancellationToken = default)
    {
        lock (_buffer)
        {
            _buffer.Enqueue(message);
            Metrics.MessagesPublished++;
            Metrics.CurrentBufferDepth = _buffer.Count;
            if (Metrics.CurrentBufferDepth > Metrics.PeakBufferDepth)
            {
                Metrics.PeakBufferDepth = Metrics.CurrentBufferDepth;
            }
        }
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<T?> TryDequeueAsync(CancellationToken cancellationToken = default)
    {
        lock (_buffer)
        {
            if (_buffer.Count > 0)
            {
                var message = _buffer.Dequeue();
                Metrics.CurrentBufferDepth = _buffer.Count;
                return Task.FromResult<T?>(message);
            }
        }
        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc/>
    public int PendingCount
    {
        get
        {
            lock (_buffer)
            {
                return _buffer.Count;
            }
        }
    }
}
