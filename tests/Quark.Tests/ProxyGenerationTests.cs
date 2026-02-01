using Moq;
using Quark.Client;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Tests for the ProxySourceGenerator and generated actor proxies.
/// </summary>
public class ProxyGenerationTests
{
    [Fact]
    public void ProxyFactory_CreateProxy_ForITestProxyActor_ReturnsProxy()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "test-actor-1";

        // Act
        var proxy = ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId);

        // Assert
        Assert.NotNull(proxy);
        Assert.Equal(actorId, proxy.ActorId);
        Assert.IsAssignableFrom<ITestProxyActor>(proxy);
    }

    [Fact]
    public void ProxyFactory_CreateProxy_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ActorProxyFactory.CreateProxy<ITestProxyActor>(null!, "test-actor"));
    }

    [Fact]
    public void ProxyFactory_CreateProxy_WithNullActorId_ThrowsArgumentNullException()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, null!));
    }

    [Fact]
    public async Task IClusterClient_GetActor_ReturnsTypeSafeProxy()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "test-actor-2";
        
        // Setup GetActor to use ActorProxyFactory
        mockClient.Setup(c => c.GetActor<ITestProxyActor>(actorId))
            .Returns(() => ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId));

        // Act
        var proxy = mockClient.Object.GetActor<ITestProxyActor>(actorId);

        // Assert
        Assert.NotNull(proxy);
        Assert.Equal(actorId, proxy.ActorId);
        Assert.IsAssignableFrom<ITestProxyActor>(proxy);
        
        // Verify lifecycle methods don't throw (client proxies have empty implementations)
        await proxy.OnActivateAsync();
        await proxy.OnDeactivateAsync();
    }

    [Fact]
    public async Task Proxy_IncrementAsync_SendsCorrectEnvelope()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "counter-1";
        var amount = 42;
        
        QuarkEnvelope? capturedEnvelope = null;
        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<QuarkEnvelope, CancellationToken>((env, ct) => capturedEnvelope = env)
            .ReturnsAsync(new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "TestProxyActor",
                "IncrementAsync",
                Array.Empty<byte>()));

        var proxy = ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId);

        // Act
        await proxy.IncrementAsync(amount);

        // Assert
        Assert.NotNull(capturedEnvelope);
        Assert.Equal(actorId, capturedEnvelope.ActorId);
        Assert.Equal("TestProxyActor", capturedEnvelope.ActorType);
        Assert.Equal("IncrementAsync", capturedEnvelope.MethodName);
        Assert.NotEmpty(capturedEnvelope.Payload); // Protobuf serialized request
        
        mockClient.Verify(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Proxy_GetCountAsync_DeserializesResponse()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "counter-2";
        var expectedCount = 123;

        // Create a response envelope with serialized GetCountAsyncResponse
        var responseMessage = new Generated.GetCountAsyncResponse { Result = expectedCount };
        byte[] responsePayload;
        using (var ms = new MemoryStream())
        {
            ProtoBuf.Serializer.Serialize(ms, responseMessage);
            responsePayload = ms.ToArray();
        }

        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuarkEnvelope env, CancellationToken ct) => new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "TestProxyActor",
                "GetCountAsync",
                Array.Empty<byte>())
            {
                ResponsePayload = responsePayload
            });

        var proxy = ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId);

        // Act
        var result = await proxy.GetCountAsync();

        // Assert
        Assert.Equal(expectedCount, result);
        mockClient.Verify(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Proxy_ProcessMessageAsync_WithMultipleParameters_WorksCorrectly()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "processor-1";
        var message = "Hello, World!";
        var priority = 5;
        var expectedResponse = "Processed: Hello, World!";

        // Create a response envelope
        var responseMessage = new Generated.ProcessMessageAsyncResponse { Result = expectedResponse };
        byte[] responsePayload;
        using (var ms = new MemoryStream())
        {
            ProtoBuf.Serializer.Serialize(ms, responseMessage);
            responsePayload = ms.ToArray();
        }

        QuarkEnvelope? capturedEnvelope = null;
        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<QuarkEnvelope, CancellationToken>((env, ct) => capturedEnvelope = env)
            .ReturnsAsync((QuarkEnvelope env, CancellationToken ct) => new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "TestProxyActor",
                "ProcessMessageAsync",
                Array.Empty<byte>())
            {
                ResponsePayload = responsePayload
            });

        var proxy = ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId);

        // Act
        var result = await proxy.ProcessMessageAsync(message, priority);

        // Assert
        Assert.Equal(expectedResponse, result);
        Assert.NotNull(capturedEnvelope);
        Assert.Equal("ProcessMessageAsync", capturedEnvelope.MethodName);
        Assert.NotEmpty(capturedEnvelope.Payload); // Contains serialized request with both parameters
        
        // Verify the request was serialized correctly
        using (var ms = new MemoryStream(capturedEnvelope.Payload))
        {
            var request = ProtoBuf.Serializer.Deserialize<Generated.ProcessMessageAsyncRequest>(ms);
            Assert.Equal(message, request.Message);
            Assert.Equal(priority, request.Priority);
        }
    }

    [Fact]
    public async Task Proxy_ResetAsync_WithoutReturnValue_WorksCorrectly()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "resettable-1";

        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "TestProxyActor",
                "ResetAsync",
                Array.Empty<byte>()));

        var proxy = ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId);

        // Act
        await proxy.ResetAsync();

        // Assert
        mockClient.Verify(c => c.SendAsync(
            It.Is<QuarkEnvelope>(e => e.MethodName == "ResetAsync"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Proxy_WhenServerReturnsError_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "failing-actor";
        var errorMessage = "Actor not found";

        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "TestProxyActor",
                "GetCountAsync",
                Array.Empty<byte>())
            {
                IsError = true,
                ErrorMessage = errorMessage
            });

        var proxy = ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.GetCountAsync());
        Assert.Contains(errorMessage, ex.Message);
    }

    [Fact]
    public async Task Proxy_WhenExpectedResponsePayloadMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "incomplete-actor";

        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "TestProxyActor",
                "GetCountAsync",
                Array.Empty<byte>())
            {
                ResponsePayload = null // Missing expected response
            });

        var proxy = ActorProxyFactory.CreateProxy<ITestProxyActor>(mockClient.Object, actorId);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.GetCountAsync());
        Assert.Contains("Expected response payload", ex.Message);
    }
}
