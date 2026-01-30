// Copyright (c) Quark Framework. All rights reserved.

using System.Threading.Channels;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Backpressure strategy that blocks publishers when buffer is full.
/// Phase 8.5: Provides guaranteed delivery by slowing down publishers.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal sealed class BlockStrategy<T> : ChannelBackpressureStrategy<T>
{
    public BlockStrategy(StreamBackpressureOptions options) : base(options)
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
        var wasBlocked = PendingCount >= _options.BufferSize;
        
        await _buffer.Writer.WriteAsync(message, cancellationToken);

        if (_options.EnableMetrics)
        {
            Metrics.MessagesPublished++;
            if (wasBlocked)
            {
                Metrics.ThrottleEvents++;
            }
            UpdateBufferMetrics();
        }

        return true;
    }
}
