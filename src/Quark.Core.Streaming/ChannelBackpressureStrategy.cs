// Copyright (c) Quark Framework. All rights reserved.

using System.Threading.Channels;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Base class for backpressure strategies that use a channel-based buffer.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal abstract class ChannelBackpressureStrategy<T> : IStreamBackpressureStrategy<T>
{
    protected readonly Channel<T> _buffer;
    protected readonly StreamBackpressureOptions _options;

    protected ChannelBackpressureStrategy(StreamBackpressureOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Metrics = new StreamBackpressureMetrics();
        _buffer = CreateChannel();
    }

    /// <inheritdoc/>
    public StreamBackpressureMetrics Metrics { get; }

    /// <inheritdoc/>
    public abstract Task<bool> TryPublishAsync(T message, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public async Task<T?> TryDequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_buffer.Reader.TryRead(out var message))
        {
            UpdateBufferMetrics();
            return message;
        }

        return default;
    }

    /// <inheritdoc/>
    public int PendingCount => _buffer.Reader.Count;

    protected abstract Channel<T> CreateChannel();

    protected void UpdateBufferMetrics()
    {
        if (!_options.EnableMetrics)
            return;

        Metrics.CurrentBufferDepth = PendingCount;
        if (Metrics.CurrentBufferDepth > Metrics.PeakBufferDepth)
        {
            Metrics.PeakBufferDepth = Metrics.CurrentBufferDepth;
        }
        Metrics.LastUpdated = DateTimeOffset.UtcNow;
    }
}
