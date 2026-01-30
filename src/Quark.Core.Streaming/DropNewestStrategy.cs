// Copyright (c) Quark Framework. All rights reserved.

using System.Threading.Channels;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Backpressure strategy that drops the newest messages when buffer is full.
/// Phase 8.5: Preserves older messages at the cost of rejecting new ones.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal sealed class DropNewestStrategy<T> : ChannelBackpressureStrategy<T>
{
    public DropNewestStrategy(StreamBackpressureOptions options) : base(options)
    {
    }

    protected override Channel<T> CreateChannel()
    {
        return Channel.CreateBounded<T>(new BoundedChannelOptions(_options.BufferSize)
        {
            FullMode = BoundedChannelFullMode.DropNewest,
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
            if (hadSpace)
            {
                Metrics.MessagesPublished++;
            }
            else
            {
                Metrics.MessagesDropped++; // This new message was dropped
            }
            UpdateBufferMetrics();
        }

        return hadSpace;
    }
}
