using System.Collections.Concurrent;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Quark.Abstractions.Clustering;
using Quark.Networking.Abstractions;
using GrpcChannel = Grpc.Net.Client.GrpcChannel;

namespace Quark.Transport.Grpc;

/// <summary>
///     gRPC-based implementation of IQuarkTransport using bi-directional streaming.
///     Maintains one persistent stream per silo connection.
///     Supports optional channel pooling for efficient connection management.
///     Optimizes local calls to avoid network overhead when target is the local silo.
/// </summary>
public sealed class GrpcQuarkTransport : IQuarkTransport, IEnvelopeReceiver
{
    private readonly ConcurrentDictionary<string, SiloConnection> _connections = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<QuarkEnvelope>> _pendingRequests = new();
    private readonly IQuarkChannelEnvelopeQueue _quarkChannelEnvelopeQueue;
    private readonly GrpcChannelPool? _channelPool;
    private readonly TimeSpan _requestTimeout;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GrpcQuarkTransport" /> class.
    /// </summary>
    /// <param name="localSiloId">The local silo ID.</param>
    /// <param name="localEndpoint">The local endpoint (host:port).</param>
    /// <param name="quarkChannelEnvelopeQueue">The envelope queue for outgoing messages.</param>
    /// <param name="channelPool">Optional channel pool for connection reuse and lifecycle management.</param>
    /// <param name="requestTimeout">Optional timeout for requests. Defaults to 30 seconds.</param>
    public GrpcQuarkTransport(string localSiloId, string localEndpoint,
        IQuarkChannelEnvelopeQueue quarkChannelEnvelopeQueue, GrpcChannelPool? channelPool = null,
        TimeSpan? requestTimeout = null)
    {
        LocalSiloId = localSiloId ?? throw new ArgumentNullException(nameof(localSiloId));
        LocalEndpoint = localEndpoint ?? throw new ArgumentNullException(nameof(localEndpoint));
        _quarkChannelEnvelopeQueue = quarkChannelEnvelopeQueue;
        _channelPool = channelPool;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public string LocalSiloId { get; }

    /// <inheritdoc />
    public string LocalEndpoint { get; }

    /// <inheritdoc />
    public event EventHandler<QuarkEnvelope>? EnvelopeReceived;

    /// <inheritdoc />
    public async Task<QuarkEnvelope> SendAsync(
        string targetSiloId,
        QuarkEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        // OPTIMIZATION: Local call detection - avoid network serialization/deserialization
        // If target is the local silo, use in-memory dispatch via EnvelopeReceived event
        // 
        // NOTE: This optimization requires the silo infrastructure to subscribe to the
        // EnvelopeReceived event and call SendResponse when processing completes.
        // When deployed standalone (client-only mode), this optimization won't apply
        // since there's no local silo to handle the envelope.
        if (targetSiloId == LocalSiloId && EnvelopeReceived != null)
        {
            // Create TCS for response before dispatching to avoid race condition
            var tcs = new TaskCompletionSource<QuarkEnvelope>();
            _pendingRequests[envelope.MessageId] = tcs;

            try
            {
                // Dispatch locally via event - this triggers local actor invocation
                // The silo infrastructure handles this event and calls SendResponse with the result
                EnvelopeReceived.Invoke(this, envelope);

                // Wait for response with timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_requestTimeout);

                return await tcs.Task.WaitAsync(cts.Token);
            }
            finally
            {
                _pendingRequests.TryRemove(envelope.MessageId, out _);
            }
        }

        // Create TCS for response
        var remoteTcs = new TaskCompletionSource<QuarkEnvelope>();
        _pendingRequests[envelope.MessageId] = remoteTcs;


        // Send via stream
        await _quarkChannelEnvelopeQueue.Outgoing.Writer.WriteAsync(envelope, cancellationToken);
        
        // Wait for response with configured timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_requestTimeout);

        return await remoteTcs.Task.WaitAsync(timeoutCts.Token);
    }

    /// <inheritdoc />
    public async Task ConnectAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_connections.ContainsKey(siloInfo.SiloId))
            return; // Already connected

        // Use channel pool if available, otherwise create a new channel
        var endpoint = $"http://{siloInfo.Endpoint}";
        var channel = _channelPool?.GetOrCreateChannel(endpoint) ?? GrpcChannel.ForAddress(endpoint);
        var client = new QuarkTransport.QuarkTransportClient(channel);

        // Start bi-directional stream
        var call = client.ActorStream(cancellationToken: cancellationToken);

        var connection = new SiloConnection(siloInfo.SiloId, channel, call, _channelPool != null);
        _connections[siloInfo.SiloId] = connection;
        _ = Task.Run(async () =>
        {
            await foreach (var envelope in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                try
                {
                    OnEnvelopeReceived(EnvelopeMessageConverter.FromProtoMessage(envelope));
                }
                catch (Exception)
                {
                    // Ignore malformed messages
                }
            }
        });
        _ = Task.Run(async () =>
        {
            await foreach (var envelope in _quarkChannelEnvelopeQueue.Outgoing.Reader.ReadAllAsync(cancellationToken))
            {
                await connection.Call.RequestStream.WriteAsync(EnvelopeMessageConverter.ToProtoMessage(envelope), cancellationToken);
            }
        });
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(string siloId)
    {
        if (_connections.TryRemove(siloId, out var connection))
        {
            await connection.Call.RequestStream.CompleteAsync();

            // Only dispose channel if not managed by pool
            if (!connection.IsPooled)
            {
                connection.Channel.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        // For ASP.NET Core hosting, the server would be started separately
        // This implementation focuses on the client-side connection management
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Close all connections
        foreach (var connection in _connections.Values)
        {
            await connection.Call.RequestStream.CompleteAsync();

            // Only dispose channel if not managed by pool
            if (!connection.IsPooled)
            {
                connection.Channel.Dispose();
            }
        }

        _connections.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void SendResponse(QuarkEnvelope responseEnvelope)
    {
        // Complete the pending request with the response (for local calls)
        if (_pendingRequests.TryRemove(responseEnvelope.MessageId, out var tcs))
        {
            tcs.SetResult(responseEnvelope);
        }

        _quarkChannelEnvelopeQueue.Outgoing.Writer.TryWrite(responseEnvelope);
    }

    /// <summary>
    /// Notifies the transport of an incoming envelope from the gRPC layer.
    /// Implements IEnvelopeReceiver for decoupled communication with QuarkTransportService.
    /// </summary>
    /// <param name="envelope">The received envelope.</param>
    public void OnEnvelopeReceived(QuarkEnvelope envelope)
    {
        // Complete pending request if this is a response to an outgoing call
        if (_pendingRequests.TryRemove(envelope.MessageId, out var tcs))
        {
            tcs.SetResult(envelope);
        }
    }

    private sealed class SiloConnection
    {
        public SiloConnection(string siloId, GrpcChannel channel,
            AsyncDuplexStreamingCall<EnvelopeMessage, EnvelopeMessage> call, bool isPooled)
        {
            SiloId = siloId;
            Channel = channel;
            Call = call;
            IsPooled = isPooled;
        }

        public string SiloId { get; }
        public GrpcChannel Channel { get; }
        public AsyncDuplexStreamingCall<EnvelopeMessage, EnvelopeMessage> Call { get; }
        public bool IsPooled { get; }
        public IClientStreamWriter<EnvelopeMessage> RequestStream => Call.RequestStream;
    }
}