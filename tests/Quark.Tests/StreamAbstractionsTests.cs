using Quark.Abstractions.Streaming;

namespace Quark.Tests;

/// <summary>
/// Tests for the streaming abstractions (StreamId, QuarkStreamAttribute, etc.)
/// </summary>
public class StreamAbstractionsTests
{
    [Fact]
    public void StreamId_Constructor_WithValidParameters_CreatesStreamId()
    {
        // Arrange & Act
        var streamId = new StreamId("orders/processed", "order-123");

        // Assert
        Assert.Equal("orders/processed", streamId.Namespace);
        Assert.Equal("order-123", streamId.Key);
        Assert.Equal("orders/processed/order-123", streamId.FullId);
    }

    [Fact]
    public void StreamId_Constructor_WithNullNamespace_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new StreamId(null!, "key"));
    }

    [Fact]
    public void StreamId_Constructor_WithEmptyNamespace_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new StreamId("", "key"));
    }

    [Fact]
    public void StreamId_Constructor_WithNullKey_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new StreamId("namespace", null!));
    }

    [Fact]
    public void StreamId_Equals_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var streamId1 = new StreamId("orders/processed", "order-123");
        var streamId2 = new StreamId("orders/processed", "order-123");

        // Act & Assert
        Assert.Equal(streamId1, streamId2);
        Assert.True(streamId1 == streamId2);
        Assert.False(streamId1 != streamId2);
    }

    [Fact]
    public void StreamId_Equals_WithDifferentValues_ReturnsFalse()
    {
        // Arrange
        var streamId1 = new StreamId("orders/processed", "order-123");
        var streamId2 = new StreamId("orders/processed", "order-456");

        // Act & Assert
        Assert.NotEqual(streamId1, streamId2);
        Assert.False(streamId1 == streamId2);
        Assert.True(streamId1 != streamId2);
    }

    [Fact]
    public void StreamId_GetHashCode_WithSameValues_ReturnsSameHashCode()
    {
        // Arrange
        var streamId1 = new StreamId("orders/processed", "order-123");
        var streamId2 = new StreamId("orders/processed", "order-123");

        // Act & Assert
        Assert.Equal(streamId1.GetHashCode(), streamId2.GetHashCode());
    }

    [Fact]
    public void StreamId_ToString_ReturnsFullId()
    {
        // Arrange
        var streamId = new StreamId("orders/processed", "order-123");

        // Act
        var result = streamId.ToString();

        // Assert
        Assert.Equal("orders/processed/order-123", result);
    }

    [Fact]
    public void QuarkStreamAttribute_Constructor_WithValidNamespace_CreatesAttribute()
    {
        // Arrange & Act
        var attribute = new QuarkStreamAttribute("orders/processed");

        // Assert
        Assert.Equal("orders/processed", attribute.Namespace);
    }

    [Fact]
    public void QuarkStreamAttribute_Constructor_WithNullNamespace_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new QuarkStreamAttribute(null!));
    }

    [Fact]
    public void QuarkStreamAttribute_Constructor_WithEmptyNamespace_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new QuarkStreamAttribute(""));
    }
}
