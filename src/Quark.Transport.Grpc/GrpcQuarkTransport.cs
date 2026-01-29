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
/// </summary>
public sealed class GrpcQuarkTransport : IQuarkTransport
{
    private readonly ConcurrentDictionary<string, SiloConnection> _connections = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<QuarkEnvelope>> _pendingRequests = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="GrpcQuarkTransport" /> class.
    /// </summary>
    /// <param name="localSiloId">The local silo ID.</param>
    /// <param name="localEndpoint">The local endpoint (host:port).</param>
    public GrpcQuarkTransport(string localSiloId, string localEndpoint)
    {
        LocalSiloId = localSiloId ?? throw new ArgumentNullException(nameof(localSiloId));
        LocalEndpoint = localEndpoint ?? throw new ArgumentNullException(nameof(localEndpoint));
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
        if (!_connections.TryGetValue(targetSiloId, out var connection))
            throw new InvalidOperationException($"No connection to silo {targetSiloId}. Call ConnectAsync first.");

        // Create TCS for response
        var tcs = new TaskCompletionSource<QuarkEnvelope>();
        _pendingRequests[envelope.MessageId] = tcs;

        try
        {
            // Convert to protobuf message
            var protoMessage = ToProtoMessage(envelope);

            // Send via stream
            await connection.RequestStream.WriteAsync(protoMessage, cancellationToken);

            // Wait for response (30s timeout)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            _pendingRequests.TryRemove(envelope.MessageId, out _);
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default)
    {
        if (_connections.ContainsKey(siloInfo.SiloId))
            return; // Already connected

        var channel = GrpcChannel.ForAddress($"http://{siloInfo.Endpoint}");
        var client = new QuarkTransport.QuarkTransportClient(channel);

        // Start bi-directional stream
        var call = client.ActorStream(cancellationToken: cancellationToken);

        var connection = new SiloConnection(siloInfo.SiloId, channel, call);
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
            connection.Channel.Dispose();
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
            connection.Channel.Dispose();
        }

        _connections.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
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
                else
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
            ErrorMessage = envelope.ErrorMessage ?? string.Empty
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
            message.CorrelationId)
        {
            ResponsePayload = message.ResponsePayload.Length > 0 ? message.ResponsePayload.ToByteArray() : null,
            IsError = message.IsError,
            ErrorMessage = message.ErrorMessage
        };
    }

    private sealed class SiloConnection
    {
        public SiloConnection(string siloId, GrpcChannel channel,
            AsyncDuplexStreamingCall<EnvelopeMessage, EnvelopeMessage> call)
        {
            SiloId = siloId;
            Channel = channel;
            Call = call;
        }

        public string SiloId { get; }
        public GrpcChannel Channel { get; }
        public AsyncDuplexStreamingCall<EnvelopeMessage, EnvelopeMessage> Call { get; }
        public IClientStreamWriter<EnvelopeMessage> RequestStream => Call.RequestStream;
    }
}