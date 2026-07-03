using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Quark.Transport.Abstractions;

namespace Quark.Runtime.Clustering;

/// <summary>
///     Manages a single pooled, multiplexed TCP connection to a peer silo.
///     Silo-to-silo analogue of <c>TcpGatewayConnection</c>; built on the
///     <see cref="ITransport" /> abstraction so it does not reference any concrete transport type.
///     Dials lazily on first use; single-flights concurrent first connects.
/// </summary>
public sealed class SiloPeerConnection : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly MessageSerializer _serializer;
    private readonly SiloAddress _peer;
    private readonly ILogger<SiloPeerConnection>? _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<MessageEnvelope>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private ITransportConnection? _connection;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;
    private Task? _executeLoop;
    private bool _disposed;

    public SiloPeerConnection(ITransport transport, MessageSerializer serializer,
        SiloAddress peer, ILogger<SiloPeerConnection>? logger = null)
    {
        _transport = transport;
        _serializer = serializer;
        _peer = peer;
        _logger = logger;
    }

    /// <summary>Idempotent lazy connect; safe under concurrent first calls (single-flight).</summary>
    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_connection is not null) return;

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection is not null) return;
            EndPoint endpoint = ResolveEndPoint(_peer);
            _connection = await _transport.ConnectAsync(endpoint, ct).ConfigureAwait(false);
            _readCts = new CancellationTokenSource();
            _executeLoop = _connection.ExecuteAsync(_readCts.Token);
            _readLoop = ReadLoopAsync(_readCts.Token);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<MessageEnvelope> SendAndAwaitAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);

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
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
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

    internal void FaultAllPending(Exception ex)
    {
        foreach (long key in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(key, out TaskCompletionSource<MessageEnvelope>? tcs))
                tcs.TrySetException(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        CancellationTokenSource? cts = Interlocked.Exchange(ref _readCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            if (_connection is not null)
                await _connection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            if (_readLoop is not null)
            {
                try { await _readLoop.ConfigureAwait(false); } catch { }
            }
            if (_executeLoop is not null)
            {
                try { await _executeLoop.ConfigureAwait(false); } catch { }
            }
            cts.Dispose();
        }

        FaultAllPending(new InvalidOperationException($"Peer connection to {_peer} closed."));

        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _connectLock.Dispose();
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
        FaultAllPending(new InvalidOperationException($"Peer connection to {_peer} closed."));
    }

    private static EndPoint ResolveEndPoint(SiloAddress address)
    {
        if (IPAddress.TryParse(address.Host, out IPAddress? ipAddress))
            return new IPEndPoint(ipAddress, address.Port);

        IPAddress resolved = Dns.GetHostAddresses(address.Host)
                                 .FirstOrDefault(static ip => ip.AddressFamily == AddressFamily.InterNetwork)
                             ?? IPAddress.Loopback;
        return new IPEndPoint(resolved, address.Port);
    }
}
