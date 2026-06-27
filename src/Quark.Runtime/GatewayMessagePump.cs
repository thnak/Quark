using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Diagnostics.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Streaming.Abstractions;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Long-running pump that accepts framed messages from external TCP client connections
///     and dispatches them through <see cref="IMessageDispatcher" />.
///     Listens on <see cref="SiloRuntimeOptions.GatewayAddress" /> (port 30000 by default).
///     Registered only when <c>UseLocalhostClustering()</c> is called.
/// </summary>
public sealed class GatewayMessagePump : IHostedService, IAsyncDisposable
{
    private readonly List<Task> _connectionTasks = [];
    private readonly IMessageDispatcher _dispatcher;
    private readonly ILogger<GatewayMessagePump> _logger;
    private readonly SiloRuntimeOptions _options;
    private readonly MessageSerializer _serializer;
    private readonly IServiceProvider _services;

    // Optional streaming dependencies — resolved lazily in StartAsync.
    private IUntypedStreamSubscriptionRegistry? _streamRegistry;
    private GatewayClientSubscriptionTable? _subTable;
    private ICodecProvider? _codecs;
    private TcpClientObserverTable? _tcpObserverTable;
    private IQuarkDiagnosticListener _diagnostics = NullDiagnosticListener.Instance;

    private Task? _acceptLoop;
    private CancellationTokenSource? _cts;
    private ITransportListener? _listener;
    private int _activeConnectionCount;

    public GatewayMessagePump(
        IServiceProvider services,
        MessageSerializer serializer,
        IMessageDispatcher dispatcher,
        IOptions<SiloRuntimeOptions> options,
        ILogger<GatewayMessagePump> logger)
    {
        _services = services;
        _serializer = serializer;
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_acceptLoop is not null)
        {
            return;
        }

        // Resolve optional streaming dependencies lazily at start time, outside DI singleton
        // construction context, to avoid a re-entrancy deadlock on the DI root scope lock.
        _streamRegistry = _services.GetService<IUntypedStreamSubscriptionRegistry>();
        _subTable = _services.GetService<GatewayClientSubscriptionTable>();
        _codecs = _services.GetService<ICodecProvider>();
        _tcpObserverTable = _services.GetService<TcpClientObserverTable>();
        _diagnostics = _services.GetService<IQuarkDiagnosticListener>() ?? NullDiagnosticListener.Instance;
        var transport = _services.GetService(typeof(ITransport)) as ITransport;
        if (transport is null)
        {
            _logger.LogDebug("No transport registered; gateway message pump remains idle.");
            return;
        }

        EndPoint endPoint = ResolveEndPoint(_options.GatewayAddress);
        _listener = transport.CreateListener(endPoint);
        await _listener.BindAsync(cancellationToken).ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);

        _logger.LogInformation("Gateway message pump listening on {Address}.", _options.GatewayAddress);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts = _cts;
        if (cts != null)
        {
            await cts.CancelAsync();
        }

        if (_listener is not null)
        {
            await _listener.StopAsync(cancellationToken).ConfigureAwait(false);
            await _listener.DisposeAsync().ConfigureAwait(false);
            _listener = null;
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts is { IsCancellationRequested: true })
            {
            }

            _acceptLoop = null;
        }

        Task[] remaining;
        lock (_connectionTasks)
        {
            remaining = _connectionTasks.ToArray();
        }

        try
        {
            await Task.WhenAll(remaining).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts is { IsCancellationRequested: true })
        {
            // Normal shutdown — connection tasks were cancelled.
        }
        cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ProcessConnectionAsync(ITransportConnection connection,
        CancellationToken cancellationToken)
    {
        int count = Interlocked.Increment(ref _activeConnectionCount);
        long connectedAt = Stopwatch.GetTimestamp();
        QuarkInstruments.ActiveGatewayConnections.Add(1);
        _diagnostics.OnConnectionAccepted(new ConnectionAcceptedEvent(
            connection.ConnectionId, connection.RemoteEndPoint, count));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task executeTask = connection.ExecuteAsync(linkedCts.Token);

        using var writeLock = new SemaphoreSlim(1, 1);
        var connectionSubscriptions = new List<Guid>();
        var connectionObservers = new List<GrainId>();
        Exception? connectionError = null;

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                MessageEnvelope? envelope = await _serializer.ReadAsync(connection.Transport.Input, linkedCts.Token)
                    .ConfigureAwait(false);
                if (envelope is null)
                {
                    break;
                }

                QuarkInstruments.GatewayMessagesReceived.Add(1,
                    new KeyValuePair<string, object?>("message_type", envelope.MessageType.ToString()));

                switch (envelope.MessageType)
                {
                    case MessageType.StreamSubscribe:
                        await HandleStreamSubscribeAsync(
                            envelope, connection.Transport.Output, writeLock,
                            connectionSubscriptions, linkedCts.Token).ConfigureAwait(false);
                        break;

                    case MessageType.StreamUnsubscribe:
                        HandleStreamUnsubscribe(envelope, connectionSubscriptions);
                        break;

                    case MessageType.ObserverRegister:
                        HandleObserverRegister(envelope, connection.Transport.Output, writeLock,
                            connectionObservers, linkedCts.Token, connection.RemoteEndPoint);
                        break;

                    case MessageType.ObserverUnregister:
                        HandleObserverUnregister(envelope, connectionObservers);
                        break;

                    case MessageType.Request:
                    case MessageType.Response:
                    case MessageType.OneWayRequest:
                    case MessageType.System:
                    case MessageType.StreamPush:
                    case MessageType.ObserverInvoke:
                    default:
                        long dispatchStart = Stopwatch.GetTimestamp();
                        MessageEnvelope? response;
                        Exception? dispatchError = null;
                        try
                        {
                            response = await _dispatcher.DispatchAsync(envelope, linkedCts.Token)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            dispatchError = ex;
                            throw;
                        }
                        finally
                        {
                            // Best-effort GrainId extraction — envelope.Headers may carry it.
                            GrainId grainId = default;
                            _diagnostics.OnMessageDispatched(new MessageDispatchedEvent(
                                connection.ConnectionId, grainId, envelope.MessageType,
                                Stopwatch.GetElapsedTime(dispatchStart), dispatchError is null));
                        }

                        if (response is not null)
                        {
                            await writeLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                            try
                            {
                                await _serializer.WriteAsync(connection.Transport.Output, response, linkedCts.Token)
                                    .ConfigureAwait(false);
                            }
                            finally
                            {
                                writeLock.Release();
                            }
                        }

                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            connectionError = ex;
            throw;
        }
        finally
        {
            // Cancel first so in-flight write-back lambdas get OperationCanceledException,
            // not ObjectDisposedException from the disposed writeLock.
            await linkedCts.CancelAsync();

            Interlocked.Decrement(ref _activeConnectionCount);
            QuarkInstruments.ActiveGatewayConnections.Add(-1);
            _diagnostics.OnConnectionClosed(new ConnectionClosedEvent(
                connection.ConnectionId, connection.RemoteEndPoint,
                Stopwatch.GetElapsedTime(connectedAt), connectionError));

            // Clean up observer registrations created by this connection.
            if (connectionObservers.Count > 0)
            {
                _tcpObserverTable?.RemoveAll(connectionObservers);

                foreach (GrainId id in connectionObservers)
                {
                    _diagnostics.OnObserverDeregistered(new ObserverDeregisteredEvent(id));
                }
            }

            // Clean up all subscriptions created by this connection.
            if (connectionSubscriptions.Count > 0)
            {
                foreach (Guid subId in connectionSubscriptions)
                {
                    _streamRegistry?.UnsubscribeUntyped(subId);
                }

                _subTable?.RemoveAll(connectionSubscriptions);
            }

            await connection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await executeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleStreamSubscribeAsync(
        MessageEnvelope envelope,
        PipeWriter output,
        SemaphoreSlim writeLock,
        List<Guid> connectionSubscriptions,
        CancellationToken cancellationToken)
    {
        if (_streamRegistry is null || _subTable is null || _codecs is null)
        {
            _logger.LogWarning("StreamSubscribe received but streaming services are not registered; ignoring.");
            return;
        }

        string? ns = envelope.Headers?.Get("stream-ns");
        string? key = envelope.Headers?.Get("stream-key");
        string? subIdStr = envelope.Headers?.Get("sub-id");

        if (ns is null || key is null || subIdStr is null || !Guid.TryParse(subIdStr, out Guid subId))
        {
            _logger.LogWarning("StreamSubscribe envelope is missing required headers (stream-ns, stream-key, sub-id); ignoring.");
            return;
        }

        var streamId = StreamId.Create(ns, key);

        // Push delegate: serializes payload and writes a StreamPush envelope back to client.
        // Captured variables are all connection-scoped; writeLock serialises concurrent pushes.
        var sub = new GatewayClientSubscription(subId, streamId, _codecs,
            async (payload, token) =>
            {
                var pushHeaders = new MessageHeaders();
                pushHeaders.Set("stream-ns", ns);
                pushHeaders.Set("stream-key", key);
                pushHeaders.Set("sub-id", subIdStr);
                pushHeaders.Set("seq", token?.ToString() ?? "0");

                var push = new MessageEnvelope
                {
                    MessageType = MessageType.StreamPush,
                    CorrelationId = -1,
                    Headers = pushHeaders,
                    Payload = payload
                };

                await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _serializer.WriteAsync(output, push, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    writeLock.Release();
                }
            });

        _streamRegistry.SubscribeUntyped(streamId, sub);
        _subTable.Add(sub);

        lock (connectionSubscriptions)
        {
            connectionSubscriptions.Add(subId);
        }

        // Send ack response with the same CorrelationId.
        var ack = new MessageEnvelope
        {
            MessageType = MessageType.Response,
            CorrelationId = envelope.CorrelationId,
            Headers = envelope.Headers,
            Payload = ReadOnlyMemory<byte>.Empty
        };

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _serializer.WriteAsync(output, ack, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void HandleStreamUnsubscribe(MessageEnvelope envelope, List<Guid> connectionSubscriptions)
    {
        if (_streamRegistry is null || _subTable is null)
        {
            return;
        }

        string? subIdStr = envelope.Headers?.Get("sub-id");
        if (subIdStr is null || !Guid.TryParse(subIdStr, out Guid subId))
        {
            _logger.LogWarning("StreamUnsubscribe envelope is missing or has invalid sub-id header; ignoring.");
            return;
        }

        _streamRegistry.UnsubscribeUntyped(subId);
        _subTable.Remove(subId);

        lock (connectionSubscriptions)
        {
            connectionSubscriptions.Remove(subId);
        }
    }

    private void HandleObserverRegister(
        MessageEnvelope envelope,
        PipeWriter output,
        SemaphoreSlim writeLock,
        List<GrainId> connectionObservers,
        CancellationToken cancellationToken,
        EndPoint? remoteEndPoint)
    {
        if (_tcpObserverTable is null)
        {
            return;
        }

        string? grainTypeName = envelope.Headers?.Get("grain-type");
        string? grainKey = envelope.Headers?.Get("grain-key");
        if (grainTypeName is null || grainKey is null)
        {
            _logger.LogWarning("ObserverRegister envelope is missing required headers (grain-type, grain-key); ignoring.");
            return;
        }

        var grainId = GrainId.Create(new GrainType(grainTypeName), grainKey);

        _tcpObserverTable.Register(grainId, async (methodId, argPayload, _) =>
        {
            var headers = new MessageHeaders();
            headers.Set("grain-type", grainTypeName);
            headers.Set("grain-key", grainKey);
            headers.Set("method-id", methodId.ToString());

            var invoke = new MessageEnvelope
            {
                MessageType = MessageType.ObserverInvoke,
                CorrelationId = -1,
                Headers = headers,
                Payload = argPayload
            };

            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _serializer.WriteAsync(output, invoke, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        });

        connectionObservers.Add(grainId);

        _diagnostics.OnObserverRegistered(new ObserverRegisteredEvent(grainId, remoteEndPoint));
    }

    private void HandleObserverUnregister(MessageEnvelope envelope, List<GrainId> connectionObservers)
    {
        string? grainTypeName = envelope.Headers?.Get("grain-type");
        string? grainKey = envelope.Headers?.Get("grain-key");
        if (grainTypeName is null || grainKey is null)
        {
            return;
        }

        var grainId = GrainId.Create(new GrainType(grainTypeName), grainKey);
        _tcpObserverTable?.Unregister(grainId);
        connectionObservers.Remove(grainId);
        _diagnostics.OnObserverDeregistered(new ObserverDeregisteredEvent(grainId));
    }

    private async Task AcceptLoopAsync(ITransportListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ITransportConnection? connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                break;
            }

            Task task = ProcessConnectionAsync(connection, cancellationToken);
            lock (_connectionTasks)
            {
                _connectionTasks.Add(task);
            }

            _ = task.ContinueWith(t =>
            {
                lock (_connectionTasks)
                {
                    _connectionTasks.Remove(t);
                }

                if (t.Exception is not null)
                {
                    _logger.LogWarning(t.Exception, "Gateway connection loop failed.");
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static EndPoint ResolveEndPoint(SiloAddress address)
    {
        if (IPAddress.TryParse(address.Host, out IPAddress? ipAddress))
        {
            return new IPEndPoint(ipAddress, address.Port);
        }

        IPAddress resolved = Dns.GetHostAddresses(address.Host)
                                 .FirstOrDefault(static ip => ip.AddressFamily == AddressFamily.InterNetwork)
                             ?? IPAddress.Loopback;

        return new IPEndPoint(resolved, address.Port);
    }
}
