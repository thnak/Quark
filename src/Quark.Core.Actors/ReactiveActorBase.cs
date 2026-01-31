// Copyright (c) Quark Framework. All rights reserved.

using System.Threading.Channels;
using Quark.Abstractions;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Actors;

/// <summary>
/// Base class for reactive actors that process streams with built-in backpressure and flow control.
/// Provides windowing, buffering, and stream transformation capabilities.
/// </summary>
/// <typeparam name="TIn">The type of input messages.</typeparam>
/// <typeparam name="TOut">The type of output messages.</typeparam>
public abstract class ReactiveActorBase<TIn, TOut> : ActorBase, IReactiveActor<TIn, TOut>
{
    private readonly Channel<TIn> _inputChannel;
    private readonly int _bufferSize;
    private readonly double _backpressureThreshold;
    private readonly BackpressureMode _overflowStrategy;
    private readonly bool _enableMetrics;
    private long _messagesReceived;
    private long _messagesProcessed;
    private long _messagesDropped;

    /// <summary>
    /// Gets the number of messages currently buffered.
    /// </summary>
    public int BufferedCount => _inputChannel.Reader.Count;

    /// <summary>
    /// Gets the total number of messages received.
    /// </summary>
    public long MessagesReceived => _messagesReceived;

    /// <summary>
    /// Gets the total number of messages processed.
    /// </summary>
    public long MessagesProcessed => _messagesProcessed;

    /// <summary>
    /// Gets the total number of messages dropped due to overflow.
    /// </summary>
    public long MessagesDropped => _messagesDropped;

    /// <summary>
    /// Gets a value indicating whether backpressure is currently active.
    /// </summary>
    public bool IsBackpressureActive => 
        (double)BufferedCount / _bufferSize >= _backpressureThreshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReactiveActorBase{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="actorId">The unique identifier for this actor.</param>
    /// <param name="actorFactory">The actor factory for creating child actors.</param>
    protected ReactiveActorBase(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory)
    {
        // Read configuration from attribute if present
        var attribute = GetType().GetCustomAttributes(typeof(ReactiveActorAttribute), false)
            .FirstOrDefault() as ReactiveActorAttribute;

        _bufferSize = attribute?.BufferSize ?? 1000;
        _backpressureThreshold = attribute?.BackpressureThreshold ?? 0.8;
        _overflowStrategy = attribute?.OverflowStrategy ?? BackpressureMode.Block;
        _enableMetrics = attribute?.EnableMetrics ?? true;

        // Create channel based on overflow strategy
        var channelOptions = new BoundedChannelOptions(_bufferSize)
        {
            FullMode = _overflowStrategy switch
            {
                BackpressureMode.DropOldest => BoundedChannelFullMode.DropOldest,
                BackpressureMode.DropNewest => BoundedChannelFullMode.DropNewest,
                BackpressureMode.Block => BoundedChannelFullMode.Wait,
                _ => BoundedChannelFullMode.Wait
            }
        };

        _inputChannel = Channel.CreateBounded<TIn>(channelOptions);
    }

    /// <summary>
    /// Sends a message to this reactive actor for processing.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendAsync(TIn message, CancellationToken cancellationToken = default)
    {
        if (_enableMetrics)
        {
            Interlocked.Increment(ref _messagesReceived);
        }

        var written = await _inputChannel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        if (written)
        {
            await _inputChannel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
        else if (_enableMetrics)
        {
            Interlocked.Increment(ref _messagesDropped);
        }
    }

    /// <summary>
    /// Processes an asynchronous stream of input messages and produces an asynchronous stream of output messages.
    /// Override this method to implement custom stream processing logic.
    /// </summary>
    /// <param name="stream">The input stream of messages.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An asynchronous stream of output messages.</returns>
    public abstract IAsyncEnumerable<TOut> ProcessStreamAsync(
        IAsyncEnumerable<TIn> stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts processing the input stream.
    /// Call this method to begin consuming messages from the input buffer.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when stream processing ends.</returns>
    protected async Task StartStreamProcessingAsync(CancellationToken cancellationToken = default)
    {
        var inputStream = ReadInputStreamAsync(cancellationToken);
        var outputStream = ProcessStreamAsync(inputStream, cancellationToken);

        await foreach (var output in outputStream.WithCancellation(cancellationToken))
        {
            if (_enableMetrics)
            {
                Interlocked.Increment(ref _messagesProcessed);
            }

            // Output can be published to another stream or handled by derived classes
            await OnOutputAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Called when an output message is produced.
    /// Override this method to handle output messages (e.g., publish to a stream).
    /// </summary>
    /// <param name="output">The output message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task OnOutputAsync(TOut output, CancellationToken cancellationToken = default)
    {
        // Default implementation does nothing
        // Derived classes can override to publish outputs
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads messages from the input channel as an async stream.
    /// </summary>
    private async IAsyncEnumerable<TIn> ReadInputStreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Completes the input channel, signaling no more messages will be sent.
    /// </summary>
    public void CompleteInput()
    {
        _inputChannel.Writer.Complete();
    }

    /// <summary>
    /// Called when the actor is deactivated.
    /// Completes the input channel to ensure graceful shutdown.
    /// </summary>
    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        CompleteInput();
        return base.OnDeactivateAsync(cancellationToken);
    }
}
