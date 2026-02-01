// Copyright (c) Quark Framework. All rights reserved.

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Quark.Networking.Abstractions;

namespace Quark.Transport.Grpc;

/// <summary>
/// gRPC service implementation for Quark actor transport.
/// Handles bi-directional streaming for actor invocations between silos.
/// This is the server-side counterpart to GrpcQuarkTransport client.
/// </summary>
public class QuarkTransportService : QuarkTransport.QuarkTransportBase
{
    private readonly IQuarkTransport _transport;
    private readonly ILogger<QuarkTransportService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkTransportService"/> class.
    /// </summary>
    /// <param name="transport">The local transport implementation.</param>
    /// <param name="logger">The logger instance.</param>
    public QuarkTransportService(IQuarkTransport transport, ILogger<QuarkTransportService> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles bi-directional streaming for actor invocations.
    /// Reads incoming requests from remote silos and writes responses back.
    /// </summary>
    public override async Task ActorStream(
        IAsyncStreamReader<EnvelopeMessage> requestStream,
        IServerStreamWriter<EnvelopeMessage> responseStream,
        ServerCallContext context)
    {
        var clientPeer = context.Peer; // For logging
        _logger.LogInformation("ActorStream connection established from {Peer}", clientPeer);

        try
        {
            // Subscribe to EnvelopeReceived event to send responses back
            // This handles responses from local actor invocations
            void OnEnvelopeReceived(object? sender, QuarkEnvelope envelope)
            {
                // Only send responses back through this stream
                // (responses to requests that came through this connection)
                var protoMessage = ToProtoMessage(envelope);
                
                // Write async in a fire-and-forget manner
                // The response stream is thread-safe in gRPC
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await responseStream.WriteAsync(protoMessage);
                        _logger.LogTrace("Response sent for message {MessageId}", envelope.MessageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send response for message {MessageId}", envelope.MessageId);
                    }
                });
            }

            _transport.EnvelopeReceived += OnEnvelopeReceived;

            try
            {
                // Read incoming requests from the client stream
                await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    try
                    {
                        var envelope = FromProtoMessage(message);
                        _logger.LogTrace("Received message {MessageId} for actor {ActorId}", 
                            envelope.MessageId, envelope.ActorId);

                        // Raise the EnvelopeReceived event for local processing
                        // This will be handled by QuarkSilo to invoke the target actor
                        if (_transport is GrpcQuarkTransport grpcTransport)
                        {
                            grpcTransport.RaiseEnvelopeReceived(envelope);
                        }
                        else
                        {
                            _logger.LogWarning("Transport is not GrpcQuarkTransport, cannot raise EnvelopeReceived event");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing incoming message");
                        
                        // Send error response back to client
                        var errorResponse = new EnvelopeMessage
                        {
                            MessageId = message.MessageId,
                            IsError = true,
                            ErrorMessage = ex.Message
                        };
                        await responseStream.WriteAsync(errorResponse);
                    }
                }
            }
            finally
            {
                _transport.EnvelopeReceived -= OnEnvelopeReceived;
            }

            _logger.LogInformation("ActorStream connection closed from {Peer}", clientPeer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ActorStream from {Peer}", clientPeer);
            throw;
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
            Payload = Google.Protobuf.ByteString.CopyFrom(envelope.Payload),
            CorrelationId = envelope.CorrelationId,
            Timestamp = envelope.Timestamp.ToUnixTimeMilliseconds(),
            ResponsePayload = envelope.ResponsePayload != null
                ? Google.Protobuf.ByteString.CopyFrom(envelope.ResponsePayload)
                : Google.Protobuf.ByteString.Empty,
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
}
