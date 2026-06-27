using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Quark.Runtime;
using Quark.Transport.Abstractions;
using Quark.Transport.Tcp;

namespace Quark.Client.Tcp;

/// <summary>
///     Manages a single TCP connection to a silo gateway.
///     Multiplexes concurrent grain calls over one socket using a correlation-ID pending map.
/// </summary>
public sealed class TcpGatewayConnection : IAsyncDisposable
{
    private readonly TcpTransport _transport;
    private readonly MessageSerializer _serializer;
    private readonly TcpStreamPushDispatcher? _pushDispatcher;
    private readonly TcpObserverDispatcher? _observerDispatcher;
    private readonly ILogger<TcpGatewayConnection>? _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<MessageEnvelope>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Side-channel frames (StreamPush / ObserverInvoke) carry no correlation id and run user
    // callback code. They are handed off to this queue and dispatched on a dedicated worker so
    // the read loop never blocks inside a user handler. Without this hand-off a slow (or
    // re-entrant) observer/stream callback would stall delivery of the grain-call Response frame
    // that follows it on the same socket — head-of-line blocking (issue #49). The channel is
    // unbounded so the read loop's TryWrite always succeeds immediately and never applies
    // back-pressure that would re-introduce the stall.
    private readonly Channel<MessageEnvelope> _sideChannel =
        Channel.CreateUnbounded<MessageEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    private ITransportConnection? _connection;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;
    private Task? _executeLoop;
    private Task? _dispatchLoop;

    public TcpGatewayConnection(TcpTransport transport, MessageSerializer serializer,
        TcpStreamPushDispatcher? pushDispatcher = null, TcpObserverDispatcher? observerDispatcher = null,
        ILogger<TcpGatewayConnection>? logger = null)
    {
        _transport = transport;
        _serializer = serializer;
        _pushDispatcher = pushDispatcher;
        _observerDispatcher = observerDispatcher;
        _logger = logger;
    }

    public async Task ConnectAsync(EndPoint endpoint, CancellationToken ct = default)
    {
        _connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false);
        _readCts = new CancellationTokenSource();
        _executeLoop = _connection.ExecuteAsync(_readCts.Token);
        _dispatchLoop = DispatchLoopAsync(_readCts.Token);
        _readLoop = ReadLoopAsync(_readCts.Token);
    }

    public async Task<MessageEnvelope> SendAndAwaitAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<MessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[envelope.CorrelationId] = tcs;

        bool lockTaken = false;
        try
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            lockTaken = true;
            await _serializer.WriteAsync(_connection!.Transport.Output, envelope, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(envelope.CorrelationId, out _);
            tcs.TrySetException(ex);
            throw;
        }
        finally
        {
            if (lockTaken) _writeLock.Release();
        }

        using var reg = ct.Register(static state =>
        {
            var (tcs, id, pending) = ((TaskCompletionSource<MessageEnvelope>, long,
                ConcurrentDictionary<long, TaskCompletionSource<MessageEnvelope>>))state!;
            if (pending.TryRemove(id, out _))
                tcs.TrySetCanceled();
        }, (tcs, envelope.CorrelationId, _pending));

        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task SendOneWayAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _serializer.WriteAsync(_connection!.Transport.Output, envelope, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task CloseAsync()
    {
        // Guard against double-close (StopAsync then DisposeAsync both call this).
        CancellationTokenSource? cts = Interlocked.Exchange(ref _readCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            // Stop accepting new side-channel frames so the dispatch worker drains and exits.
            _sideChannel.Writer.TryComplete();
            if (_connection is not null)
                await _connection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            if (_readLoop is not null)
            {
                try { await _readLoop.ConfigureAwait(false); }
                catch { }
            }
            if (_dispatchLoop is not null)
            {
                try { await _dispatchLoop.ConfigureAwait(false); }
                catch { }
            }
            if (_executeLoop is not null)
            {
                try { await _executeLoop.ConfigureAwait(false); }
                catch { }
            }
            cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                MessageEnvelope? response = await _serializer.ReadAsync(_connection!.Transport.Input, ct)
                    .ConfigureAwait(false);
                if (response is null) break;

                // Side-channel frames run user callbacks — hand them to the dispatch worker and
                // keep reading so the Response frame that may follow on the same socket is not
                // blocked behind a slow handler (issue #49).
                if (response.MessageType is MessageType.StreamPush or MessageType.ObserverInvoke)
                {
                    _sideChannel.Writer.TryWrite(response);
                    continue;
                }

                if (_pending.TryRemove(response.CorrelationId, out TaskCompletionSource<MessageEnvelope>? tcs))
                    tcs.TrySetResult(response);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _sideChannel.Writer.TryComplete();
            FaultAllPending(ex);
            return;
        }
        _sideChannel.Writer.TryComplete();
        FaultAllPending(new InvalidOperationException("Gateway connection closed."));
    }

    /// <summary>
    ///     Sequentially dispatches side-channel frames (StreamPush / ObserverInvoke) off the read
    ///     loop's thread. Serial processing preserves per-connection delivery order; a slow or
    ///     re-entrant handler delays only subsequent side-channel frames, never grain-call responses.
    /// </summary>
    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (MessageEnvelope frame in _sideChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (frame.MessageType == MessageType.StreamPush)
                    {
                        if (_pushDispatcher is not null)
                            await _pushDispatcher.DispatchAsync(frame).ConfigureAwait(false);
                    }
                    else if (_observerDispatcher is not null)
                    {
                        await _observerDispatcher.DispatchAsync(frame).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    // A bad side-channel frame (observer not found, user exception, …) must not
                    // crash the dispatch worker or the connection. Log and continue so the failure
                    // is diagnosable instead of silently swallowed (issue #20).
                    _logger?.LogWarning(ex,
                        "Side-channel frame dispatch failed (MessageType={MessageType}); connection continues.",
                        frame.MessageType);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void FaultAllPending(Exception ex)
    {
        foreach (long key in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(key, out TaskCompletionSource<MessageEnvelope>? tcs))
                tcs.TrySetException(ex);
        }
    }
}
