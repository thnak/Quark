using System.Collections.Concurrent;
using Google.Protobuf;
using Grpc.Core;
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
public sealed class GrpcQuarkTransport : IQuarkTransport
{
    private readonly ConcurrentDictionary<string, SiloConnection> _connections = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<QuarkEnvelope>> _pendingRequests = new();
    private readonly GrpcChannelPool? _channelPool;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GrpcQuarkTransport" /> class.
    /// </summary>
    /// <param name="localSiloId">The local silo ID.</param>
    /// <param name="localEndpoint">The local endpoint (host:port).</param>
    /// <param name="channelPool">Optional channel pool for connection reuse and lifecycle management.</param>
    public GrpcQuarkTransport(string localSiloId, string localEndpoint, GrpcChannelPool? channelPool = null)
    {
        LocalSiloId = localSiloId ?? throw new ArgumentNullException(nameof(localSiloId));
        LocalEndpoint = localEndpoint ?? throw new ArgumentNullException(nameof(localEndpoint));
        _channelPool = channelPool;
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
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                return await tcs.Task.WaitAsync(cts.Token);
            }
            finally
            {
                _pendingRequests.TryRemove(envelope.MessageId, out _);
            }
        }

        // Remote call - use gRPC
        if (!_connections.TryGetValue(targetSiloId, out var connection))
            throw new InvalidOperationException($"No connection to silo {targetSiloId}. Call ConnectAsync first.");

        // Create TCS for response
        var remoteTcs = new TaskCompletionSource<QuarkEnvelope>();
        _pendingRequests[envelope.MessageId] = remoteTcs;

        try
        {
            // Convert to protobuf message
            var protoMessage = ToProtoMessage(envelope);

            // Send via stream
            await connection.RequestStream.WriteAsync(protoMessage, cancellationToken);

            // Wait for response (30s timeout)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            return await remoteTcs.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            _pendingRequests.TryRemove(envelope.MessageId, out _);
        }
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

        // Start reading responses in background
        _ = Task.Run(async () => await ReadResponsesAsync(connection, cancellationToken), cancellationToken);
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

        // Also raise the event so subscribers (like QuarkTransportService) can send the response
        // over gRPC streams for remote calls
        responseEnvelope.IsResponse = true;
        EnvelopeReceived?.Invoke(this, responseEnvelope);
    }

    /// <summary>
    /// Raises the EnvelopeReceived event.
    /// Used by the server-side gRPC service to notify subscribers of incoming messages.
    /// </summary>
    /// <param name="envelope">The received envelope.</param>
    internal void RaiseEnvelopeReceived(QuarkEnvelope envelope)
    {
        EnvelopeReceived?.Invoke(this, envelope);
    }

    private async Task ReadResponsesAsync(SiloConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in connection.Call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                var envelope = FromProtoMessage(message);

                // Check if this is a response to our request
                if (_pendingRequests.TryRemove(envelope.MessageId, out var tcs))
                    tcs.SetResult(envelope);
                if (!envelope.IsResponse)
                    // This is a new request from remote
                    EnvelopeReceived?.Invoke(this, envelope);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading responses from {connection.SiloId}: {ex}");
        }
    }

    private static EnvelopeMessage ToProtoMessage(QuarkEnvelope envelope)
    {
        return new EnvelopeMessage
        {
            MessageId = envelope.MessageId,
            ActorId = envelope.ActorId,
            ActorType = envelope.ActorType,
            MethodName = envelope.MethodName,
            Payload = ByteString.CopyFrom(envelope.Payload),
            CorrelationId = envelope.CorrelationId,
            Timestamp = envelope.Timestamp.ToUnixTimeMilliseconds(),
            ResponsePayload = envelope.ResponsePayload != null
                ? ByteString.CopyFrom(envelope.ResponsePayload)
                : ByteString.Empty,
            IsError = envelope.IsError,
            ErrorMessage = envelope.ErrorMessage ?? string.Empty,
            IsResponse = envelope.IsResponse
        };
    }

    private static QuarkEnvelope FromProtoMessage(EnvelopeMessage message)
    {
        return new QuarkEnvelope(
            message.MessageId,
            message.ActorId,
            message.ActorType,
            message.MethodName,
            message.Payload.ToByteArray(),
            message.CorrelationId,
            message.IsResponse)
        {
            ResponsePayload = message.ResponsePayload.Length > 0 ? message.ResponsePayload.ToByteArray() : null,
            IsError = message.IsError,
            ErrorMessage = message.ErrorMessage
        };
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