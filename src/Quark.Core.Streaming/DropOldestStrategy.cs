// Copyright (c) Quark Framework. All rights reserved.

using System.Threading.Channels;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Backpressure strategy that drops the oldest messages when buffer is full.
/// Phase 8.5: Ensures new messages are always accepted at the cost of old ones.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal sealed class DropOldestStrategy<T> : ChannelBackpressureStrategy<T>
{
    public DropOldestStrategy(StreamBackpressureOptions options) : base(options)
    {
    }

    protected override Channel<T> CreateChannel()
    {
        return Channel.CreateBounded<T>(new BoundedChannelOptions(_options.BufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public override async Task<bool> TryPublishAsync(T message, CancellationToken cancellationToken = default)
    {
        var hadSpace = PendingCount < _options.BufferSize;
        
        await _buffer.Writer.WriteAsync(message, cancellationToken);

        if (_options.EnableMetrics)
        {
            Metrics.MessagesPublished++;
            if (!hadSpace)
            {
                Metrics.MessagesDropped++; // An old message was dropped
            }
            UpdateBufferMetrics();
        }

        return true;
    }
}
