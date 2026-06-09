using System.Collections.Concurrent;
using System.Net;
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
    private readonly ConcurrentDictionary<long, TaskCompletionSource<MessageEnvelope>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private ITransportConnection? _connection;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;
    private Task? _executeLoop;

    public TcpGatewayConnection(TcpTransport transport, MessageSerializer serializer,
        TcpStreamPushDispatcher? pushDispatcher = null, TcpObserverDispatcher? observerDispatcher = null)
    {
        _transport = transport;
        _serializer = serializer;
        _pushDispatcher = pushDispatcher;
        _observerDispatcher = observerDispatcher;
    }

    public async Task ConnectAsync(EndPoint endpoint, CancellationToken ct = default)
    {
        _connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false);
        _readCts = new CancellationTokenSource();
        _executeLoop = _connection.ExecuteAsync(_readCts.Token);
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
            if (_connection is not null)
                await _connection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            if (_readLoop is not null)
            {
                try { await _readLoop.ConfigureAwait(false); }
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

                if (response.MessageType == MessageType.StreamPush)
                {
                    if (_pushDispatcher is not null)
                        await _pushDispatcher.DispatchAsync(response).ConfigureAwait(false);
                    continue;
                }

                if (response.MessageType == MessageType.ObserverInvoke)
                {
                    if (_observerDispatcher is not null)
                    {
                        try
                        {
                            await _observerDispatcher.DispatchAsync(response).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // Log and continue — a bad observer frame must not crash the whole connection.
                            // (Observer not found, user exception, etc.)
                            _ = ex; // best-effort; no logger here
                        }
                    }
                    continue;
                }

                if (_pending.TryRemove(response.CorrelationId, out TaskCompletionSource<MessageEnvelope>? tcs))
                    tcs.TrySetResult(response);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FaultAllPending(ex);
            return;
        }
        FaultAllPending(new InvalidOperationException("Gateway connection closed."));
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
