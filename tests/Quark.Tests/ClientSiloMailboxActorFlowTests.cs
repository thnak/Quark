// Copyright (c) Quark Framework. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using Quark.Abstractions;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Converters;
using Quark.Client;
using Quark.Core.Actors;
using Quark.Hosting;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

/// <summary>
/// End-to-end integration tests for the complete Client → Silo → Mailbox → Actor → Response flow.
/// These tests validate the full message path WITHOUT using Kestrel/HTTP layer to isolate
/// and identify error sources in the actor invocation pipeline.
/// 
/// Flow tested:
/// 1. Client: Creates and sends QuarkEnvelope
/// 2. Transport: Receives envelope (simulated without actual gRPC)
/// 3. Silo: OnEnvelopeReceived handler processes envelope
/// 4. Dispatcher: Looks up actor method dispatcher
/// 5. Actor: Gets or creates actor instance
/// 6. Mailbox: Creates mailbox and posts message
/// 7. Mailbox: Sequential processing of message
/// 8. Dispatcher: Invokes actor method
/// 9. Response: Creates response envelope
/// 10. Transport: Sends response back to client
/// </summary>
public class ClientSiloMailboxActorFlowTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable?.Dispose();
        }
        _disposables.Clear();
    }

    #region Test Actor Definitions

    /// <summary>
    /// Simple test actor for validating successful invocations.
    /// </summary>
    public interface IFlowTestActor : IQuarkActor
    {
        [BinaryConverter(typeof(StringConverter), ParameterName = "message")]
        [BinaryConverter(typeof(StringConverter))] // Return value
        Task<string> EchoAsync(string message);
        
        [BinaryConverter(typeof(Int32Converter), ParameterName = "a", Order = 0)]
        [BinaryConverter(typeof(Int32Converter), ParameterName = "b", Order = 1)]
        [BinaryConverter(typeof(Int32Converter))] // Return value
        Task<int> AddAsync(int a, int b);
        
        [BinaryConverter(typeof(StringConverter), ParameterName = "exceptionMessage")]
        Task ThrowExceptionAsync(string exceptionMessage);
    }

    [Actor(Name = "FlowTestActor")]
    public class FlowTestActor : ActorBase, IFlowTestActor
    {
        public FlowTestActor(string actorId) : base(actorId) { }

        public Task<string> EchoAsync(string message)
        {
            return Task.FromResult($"Echo: {message}");
        }

        public Task<int> AddAsync(int a, int b)
        {
            return Task.FromResult(a + b);
        }

        public Task ThrowExceptionAsync(string exceptionMessage)
        {
            throw new InvalidOperationException(exceptionMessage);
        }
    }

    /// <summary>
    /// Stateful test actor for validating state persistence across calls.
    /// </summary>
    public interface IStatefulFlowActor : IQuarkActor
    {
        Task IncrementAsync();
        
        [BinaryConverter(typeof(Int32Converter))] // Return value
        Task<int> GetCountAsync();
        
        Task ResetAsync();
    }

    [Actor(Name = "StatefulFlowActor")]
    public class StatefulFlowActor : ActorBase, IStatefulFlowActor
    {
        private int _count = 0;

        public StatefulFlowActor(string actorId) : base(actorId) { }

        public Task IncrementAsync()
        {
            _count++;
            return Task.CompletedTask;
        }

        public Task<int> GetCountAsync()
        {
            return Task.FromResult(_count);
        }

        public Task ResetAsync()
        {
            _count = 0;
            return Task.CompletedTask;
        }
    }

    #endregion

    #region Complete Flow Tests

    [Fact]
    public async Task CompleteFlow_ClientToActorAndBack_SuccessfulInvocation()
    {
        // Arrange: Set up the complete stack without Kestrel
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-1");

        // Create envelope for actor invocation using binary serialization
        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var writer = new BinaryWriter(ms))
            {
                // Serialize "Hello World" parameter with length-prefixing
                BinaryConverterHelper.WriteWithLength(writer, new StringConverter(), "Hello World");
            }
            payload = ms.ToArray();
        }
        
        var envelope = CreateEnvelope(
            actorId: "flow-actor-1",
            actorType: "FlowTestActor",
            methodName: "EchoAsync",
            payload: payload);

        // Set up transport to capture response
        QuarkEnvelope? capturedResponse = null;
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp => capturedResponse = resp);

        // Act: Simulate the complete flow
        // 1. Client would normally call SendAsync, but we'll simulate envelope reception directly
        await Task.Delay(50); // Allow silo to fully start
        
        // 2. Simulate transport receiving envelope and raising event
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);

        // 3. Wait for mailbox processing
        await Task.Delay(500); // Allow time for mailbox to process

        // Assert: Verify response was sent
        Assert.NotNull(capturedResponse);
        Assert.Equal(envelope.MessageId, capturedResponse.MessageId);
        
        // If there's an error, output it for debugging
        if (capturedResponse.IsError)
        {
            throw new Exception($"Actor invocation failed with error: {capturedResponse.ErrorMessage}");
        }
        
        Assert.False(capturedResponse.IsError);
        Assert.NotNull(capturedResponse.ResponsePayload);
        
        // Verify the actual response payload contains expected data using binary deserialization
        using var responseMs = new MemoryStream(capturedResponse.ResponsePayload);
        using var responseReader = new BinaryReader(responseMs);
        var result = BinaryConverterHelper.ReadWithLength(responseReader, new StringConverter());
        
        Assert.NotNull(result);
        Assert.Contains("Echo: Hello World", result);
    }

    [Fact]
    public async Task CompleteFlow_StatefulActor_MaintainsStateAcrossCalls()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-2");
        var actorId = "stateful-actor-1";
        var responses = new List<QuarkEnvelope>();
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp => responses.Add(resp));

        await Task.Delay(50);

        // Act: Send multiple operations to the same actor
        // Operation 1: Increment
        var envelope1 = CreateEnvelope(actorId, "StatefulFlowActor", "IncrementAsync", Array.Empty<byte>());
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope1);
        await Task.Delay(200);

        // Operation 2: Increment again
        var envelope2 = CreateEnvelope(actorId, "StatefulFlowActor", "IncrementAsync", Array.Empty<byte>());
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope2);
        await Task.Delay(200);

        // Operation 3: Get count
        var envelope3 = CreateEnvelope(actorId, "StatefulFlowActor", "GetCountAsync", Array.Empty<byte>());
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope3);
        await Task.Delay(200);

        // Assert: All operations succeeded
        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.False(r.IsError));
        
        // The last response should contain the count value (binary serialized with length-prefix)
        Assert.NotNull(responses[2].ResponsePayload);
        using var countMs = new MemoryStream(responses[2].ResponsePayload);
        using var countReader = new BinaryReader(countMs);
        var count = BinaryConverterHelper.ReadWithLength(countReader, new Int32Converter());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CompleteFlow_ActorException_ReturnsErrorEnvelope()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-3");
        QuarkEnvelope? errorResponse = null;
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp => errorResponse = resp);

        await Task.Delay(50);

        // Act: Invoke method that throws exception using binary serialization
        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var writer = new BinaryWriter(ms))
            {
                BinaryConverterHelper.WriteWithLength(writer, new StringConverter(), "Test exception message");
            }
            payload = ms.ToArray();
        }
        
        var envelope = CreateEnvelope(
            "flow-actor-3",
            "FlowTestActor",
            "ThrowExceptionAsync",
            payload);
        
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);
        await Task.Delay(500);

        // Assert: Error response received
        Assert.NotNull(errorResponse);
        Assert.True(errorResponse.IsError);
        Assert.NotNull(errorResponse.ErrorMessage);
        Assert.Contains("Test exception message", errorResponse.ErrorMessage);
    }

    [Fact]
    public async Task CompleteFlow_InvalidActorType_ReturnsErrorResponse()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-4");
        QuarkEnvelope? errorResponse = null;
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp => errorResponse = resp);

        await Task.Delay(50);

        // Act: Send envelope with invalid actor type
        var envelope = CreateEnvelope(
            "invalid-actor",
            "NonExistentActorType",
            "SomeMethod",
            Array.Empty<byte>());
        
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);
        await Task.Delay(500);

        // Assert: Error response for missing dispatcher
        Assert.NotNull(errorResponse);
        Assert.True(errorResponse.IsError);
        Assert.Contains("No dispatcher registered", errorResponse.ErrorMessage);
    }

    [Fact]
    public async Task CompleteFlow_ConcurrentRequests_ProcessedSequentially()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-5");
        var responses = new List<QuarkEnvelope>();
        var responseLock = new object();
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp =>
            {
                lock (responseLock)
                {
                    responses.Add(resp);
                }
            });

        await Task.Delay(50);

        var actorId = "stateful-concurrent-actor";
        
        // Act: Send 10 concurrent increment operations
        for (int i = 0; i < 10; i++)
        {
            var envelope = CreateEnvelope(actorId, "StatefulFlowActor", "IncrementAsync", Array.Empty<byte>());
            transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);
        }

        // Wait for all to process
        await Task.Delay(1000);

        // Send GetCount to verify
        var getCountEnvelope = CreateEnvelope(actorId, "StatefulFlowActor", "GetCountAsync", Array.Empty<byte>());
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, getCountEnvelope);
        await Task.Delay(200);

        // Assert: All 11 operations succeeded (10 increments + 1 get)
        Assert.Equal(11, responses.Count);
        Assert.All(responses, r => Assert.False(r.IsError));
        
        // Final count should be 10 (sequential processing guaranteed)
        var finalResponse = responses[10];
        using var finalMs = new MemoryStream(finalResponse.ResponsePayload);
        using var finalReader = new BinaryReader(finalMs);
        var finalCount = BinaryConverterHelper.ReadWithLength(finalReader, new Int32Converter());
        Assert.Equal(10, finalCount);
    }

    [Fact]
    public async Task CompleteFlow_MailboxBackpressure_HandlesHighVolume()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-6");
        var responses = new List<QuarkEnvelope>();
        var responseLock = new object();
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp =>
            {
                lock (responseLock)
                {
                    responses.Add(resp);
                }
            });

        await Task.Delay(50);

        var actorId = "high-volume-actor";
        
        // Act: Send 100 operations rapidly
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            var envelope = CreateEnvelope(actorId, "StatefulFlowActor", "IncrementAsync", Array.Empty<byte>());
            tasks.Add(Task.Run(() =>
                transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope)));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(2000); // Allow time for processing

        // Assert: All messages processed successfully
        Assert.Equal(100, responses.Count);
        Assert.All(responses, r => Assert.False(r.IsError));
    }

    [Fact]
    public async Task CompleteFlow_MessageIdCorrelation_MaintainsRequestResponseMapping()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-7");
        var responsesByMessageId = new Dictionary<string, QuarkEnvelope>();
        var responseLock = new object();
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp =>
            {
                lock (responseLock)
                {
                    responsesByMessageId[resp.MessageId] = resp;
                }
            });

        await Task.Delay(50);

        // Act: Send multiple requests with tracked message IDs
        var messageIds = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var messageId = Guid.NewGuid().ToString();
            messageIds.Add(messageId);
            
            byte[] payload;
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    BinaryConverterHelper.WriteWithLength(writer, new StringConverter(), $"Message {i}");
                }
                payload = ms.ToArray();
            }
            
            var envelope = new QuarkEnvelope(
                messageId: messageId,
                actorId: $"actor-{i}",
                actorType: "FlowTestActor",
                methodName: "EchoAsync",
                payload: payload);
            
            transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);
        }

        await Task.Delay(1000);

        // Assert: Each request has corresponding response with matching message ID
        Assert.Equal(5, responsesByMessageId.Count);
        foreach (var messageId in messageIds)
        {
            Assert.True(responsesByMessageId.ContainsKey(messageId));
            Assert.Equal(messageId, responsesByMessageId[messageId].MessageId);
        }
    }

    #endregion

    #region Error Path Tests

    [Fact]
    public async Task ErrorPath_DispatcherNotFound_ReturnsError()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-8");
        QuarkEnvelope? errorResponse = null;
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp => errorResponse = resp);

        await Task.Delay(50);

        // Act: Send envelope with unregistered actor type
        var envelope = CreateEnvelope(
            "some-actor",
            "UnregisteredActorType",
            "SomeMethod",
            Array.Empty<byte>());
        
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);
        await Task.Delay(500);

        // Assert
        Assert.NotNull(errorResponse);
        Assert.True(errorResponse.IsError);
        Assert.Contains("No dispatcher registered for actor type", errorResponse.ErrorMessage);
    }

    [Fact]
    public async Task ErrorPath_MailboxProcessing_HandlesDispatcherException()
    {
        // Arrange
        var (client, silo, transport) = await SetupClientSiloStackAsync("test-silo-9");
        QuarkEnvelope? errorResponse = null;
        
        transport.Setup(t => t.SendResponse(It.IsAny<QuarkEnvelope>()))
            .Callback<QuarkEnvelope>(resp => errorResponse = resp);

        await Task.Delay(50);

        // Act: Invoke method that throws using binary serialization
        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var writer = new BinaryWriter(ms))
            {
                BinaryConverterHelper.WriteWithLength(writer, new StringConverter(), "Dispatcher exception test");
            }
            payload = ms.ToArray();
        }
        
        var envelope = CreateEnvelope(
            "error-actor",
            "FlowTestActor",
            "ThrowExceptionAsync",
            payload);
        
        transport.Raise(t => t.EnvelopeReceived += null, transport.Object, envelope);
        await Task.Delay(500);

        // Assert
        Assert.NotNull(errorResponse);
        Assert.True(errorResponse.IsError);
        Assert.Contains("Dispatcher exception test", errorResponse.ErrorMessage);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets up a complete client-silo stack without actual networking.
    /// Returns the client, silo, and mock transport for testing.
    /// </summary>
    private async Task<(ClusterClient Client, QuarkSilo Silo, Mock<IQuarkTransport> Transport)> 
        SetupClientSiloStackAsync(string siloId)
    {
        // Create mock dependencies
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        // Configure transport
        mockTransport.Setup(t => t.LocalSiloId).Returns(siloId);
        mockTransport.Setup(t => t.LocalEndpoint).Returns($"localhost:5000");
        mockTransport.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockTransport.Setup(t => t.StopAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Configure cluster membership
        var siloInfo = new SiloInfo(siloId, "localhost", 5000, SiloStatus.Active);
        mockClusterMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SiloInfo> { siloInfo });
        mockClusterMembership.Setup(m => m.GetActorSilo(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(siloId);
        mockClusterMembership.Setup(m => m.RegisterSiloAsync(It.IsAny<SiloInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockClusterMembership.Setup(m => m.UpdateHeartbeatAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockClusterMembership.Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockClusterMembership.Setup(m => m.UnregisterSiloAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockClusterMembership.Setup(m => m.StopAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Create actor factory
        var actorFactory = new ActorFactory();

        // Create silo
        var siloOptions = new QuarkSiloOptions
        {
            SiloId = siloId,
            Address = "localhost",
            Port = 5000,
            EnableReminders = false
        };

        var silo = new QuarkSilo(
            actorFactory,
            mockClusterMembership.Object,
            mockTransport.Object,
            siloOptions,
            NullLogger<QuarkSilo>.Instance);

        // Start silo
        await silo.StartAsync();

        // Create client
        var clientOptions = new ClusterClientOptions
        {
            ClientId = $"test-client-{siloId}",
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(30)
        };

        var client = new ClusterClient(
            mockClusterMembership.Object,
            mockTransport.Object,
            clientOptions,
            NullLogger<ClusterClient>.Instance);

        await client.ConnectAsync();

        // Track for disposal
        _disposables.Add(client);
        // Note: QuarkSilo doesn't implement IDisposable, but we could stop it in cleanup if needed

        return (client, silo, mockTransport);
    }

    /// <summary>
    /// Creates a test envelope for actor invocation.
    /// </summary>
    private QuarkEnvelope CreateEnvelope(string actorId, string actorType, string methodName, byte[] payload)
    {
        return new QuarkEnvelope(
            messageId: Guid.NewGuid().ToString(),
            actorId: actorId,
            actorType: actorType,
            methodName: methodName,
            payload: payload);
    }

    #endregion
}
