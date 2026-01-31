using Moq;
using Quark.Client;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Tests for context-based actor proxy registration using QuarkActorContext.
/// </summary>
public class ContextBasedProxyGenerationTests
{
    [Fact]
    public void ProxyFactory_CreateProxy_ForContextRegisteredActor_ReturnsProxy()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "external-actor-1";

        // Act - Should work even though IExternalLibraryActor doesn't inherit from IQuarkActor
        var proxy = ActorProxyFactory.CreateProxy<IExternalLibraryActor>(mockClient.Object, actorId);

        // Assert
        Assert.NotNull(proxy);
        Assert.Equal(actorId, proxy.ActorId);
        Assert.IsAssignableFrom<IExternalLibraryActor>(proxy);
    }

    [Fact]
    public void ProxyFactory_CreateProxy_ForContextRegisteredActor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ActorProxyFactory.CreateProxy<IExternalLibraryActor>(null!, "test-actor"));
    }

    [Fact]
    public void ProxyFactory_CreateProxy_ForContextRegisteredActor_WithNullActorId_ThrowsArgumentNullException()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ActorProxyFactory.CreateProxy<IExternalLibraryActor>(mockClient.Object, null!));
    }

    [Fact]
    public async Task ContextRegisteredProxy_CalculateAsync_SendsCorrectEnvelope()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "calculator-1";
        var x = 10;
        var y = 20;
        var expectedResult = 30;
        
        // Create a response envelope with serialized response
        var responseMessage = new Generated.CalculateAsyncResponse { Result = expectedResult };
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
                "ExternalLibraryActor",
                "CalculateAsync",
                Array.Empty<byte>())
            {
                ResponsePayload = responsePayload
            });

        var proxy = ActorProxyFactory.CreateProxy<IExternalLibraryActor>(mockClient.Object, actorId);

        // Act
        var result = await proxy.CalculateAsync(x, y);

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.NotNull(capturedEnvelope);
        Assert.Equal(actorId, capturedEnvelope.ActorId);
        Assert.Equal("ExternalLibraryActor", capturedEnvelope.ActorType);
        Assert.Equal("CalculateAsync", capturedEnvelope.MethodName);
        Assert.NotEmpty(capturedEnvelope.Payload);
        
        // Verify the request was serialized correctly
        using (var ms = new MemoryStream(capturedEnvelope.Payload))
        {
            var request = ProtoBuf.Serializer.Deserialize<Generated.CalculateAsyncRequest>(ms);
            Assert.Equal(x, request.X);
            Assert.Equal(y, request.Y);
        }
        
        mockClient.Verify(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ContextRegisteredProxy_PerformOperationAsync_WithoutReturnValue_WorksCorrectly()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "operation-1";
        var operation = "reset";

        QuarkEnvelope? capturedEnvelope = null;
        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<QuarkEnvelope, CancellationToken>((env, ct) => capturedEnvelope = env)
            .ReturnsAsync(new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "ExternalLibraryActor",
                "PerformOperationAsync",
                Array.Empty<byte>()));

        var proxy = ActorProxyFactory.CreateProxy<IExternalLibraryActor>(mockClient.Object, actorId);

        // Act
        await proxy.PerformOperationAsync(operation);

        // Assert
        Assert.NotNull(capturedEnvelope);
        Assert.Equal("PerformOperationAsync", capturedEnvelope.MethodName);
        Assert.NotEmpty(capturedEnvelope.Payload);
        
        // Verify the request was serialized correctly
        using (var ms = new MemoryStream(capturedEnvelope.Payload))
        {
            var request = ProtoBuf.Serializer.Deserialize<Generated.PerformOperationAsyncRequest>(ms);
            Assert.Equal(operation, request.Operation);
        }
        
        mockClient.Verify(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ContextRegisteredProxy_GetDataAsync_ReturnsCorrectResult()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "data-1";
        var id = "item-123";
        var expectedData = "Sample Data";

        // Create a response envelope
        var responseMessage = new Generated.GetDataAsyncResponse { Result = expectedData };
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
                "ExternalLibraryActor",
                "GetDataAsync",
                Array.Empty<byte>())
            {
                ResponsePayload = responsePayload
            });

        var proxy = ActorProxyFactory.CreateProxy<IExternalLibraryActor>(mockClient.Object, actorId);

        // Act
        var result = await proxy.GetDataAsync(id);

        // Assert
        Assert.Equal(expectedData, result);
        mockClient.Verify(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ContextRegisteredProxy_WhenServerReturnsError_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockClient = new Mock<IClusterClient>();
        var actorId = "failing-actor";
        var errorMessage = "Operation failed";

        mockClient.Setup(c => c.SendAsync(It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuarkEnvelope(
                Guid.NewGuid().ToString(),
                actorId,
                "ExternalLibraryActor",
                "CalculateAsync",
                Array.Empty<byte>())
            {
                IsError = true,
                ErrorMessage = errorMessage
            });

        var proxy = ActorProxyFactory.CreateProxy<IExternalLibraryActor>(mockClient.Object, actorId);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.CalculateAsync(1, 2));
        Assert.Contains(errorMessage, ex.Message);
    }
}
