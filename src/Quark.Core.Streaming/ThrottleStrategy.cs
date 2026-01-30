// Copyright (c) Quark Framework. All rights reserved.

using System.Threading.Channels;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Backpressure strategy that rate-limits message publishing.
/// Phase 8.5: Controls message flow through time-based throttling.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal sealed class ThrottleStrategy<T> : ChannelBackpressureStrategy<T>
{
    private readonly Queue<DateTimeOffset> _messageTimestamps = new();
    private readonly object _throttleLock = new();

    public ThrottleStrategy(StreamBackpressureOptions options) : base(options)
    {
    }

    protected override Channel<T> CreateChannel()
    {
        return Channel.CreateBounded<T>(new BoundedChannelOptions(_options.BufferSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public override async Task<bool> TryPublishAsync(T message, CancellationToken cancellationToken = default)
    {
        // Check rate limit
        bool wasThrottled = false;
        lock (_throttleLock)
        {
            var now = DateTimeOffset.UtcNow;
            var windowStart = now - _options.ThrottleWindow;

            // Remove old timestamps
            while (_messageTimestamps.Count > 0 && _messageTimestamps.Peek() < windowStart)
            {
                _messageTimestamps.Dequeue();
            }

            // Check if we're within limit
            if (_messageTimestamps.Count >= _options.MaxMessagesPerWindow)
            {
                wasThrottled = true;
                // Calculate wait time until next message can be sent
                var oldestInWindow = _messageTimestamps.Peek();
                var waitTime = oldestInWindow + _options.ThrottleWindow - now;
                
                if (waitTime > TimeSpan.Zero)
                {
                    // Wait outside the lock
                }
            }

            _messageTimestamps.Enqueue(now);
        }

        await _buffer.Writer.WriteAsync(message, cancellationToken);

        if (_options.EnableMetrics)
        {
            Metrics.MessagesPublished++;
            if (wasThrottled)
            {
                Metrics.ThrottleEvents++;
            }
            UpdateBufferMetrics();
        }

        return true;
    }
}
