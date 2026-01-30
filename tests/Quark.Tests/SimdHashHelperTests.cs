using Quark.Networking.Abstractions;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for Phase 8.1: SIMD-accelerated hash computation.
/// Verifies hardware-accelerated CRC32 and xxHash32 implementations.
/// </summary>
public class SimdHashHelperTests
{
    /// <summary>
    /// Tests that ComputeFastHash produces consistent results for the same input.
    /// Critical for consistent hashing - same key must always produce same hash.
    /// </summary>
    [Fact]
    public void ComputeFastHash_SameInput_ProducesSameHash()
    {
        // Arrange
        var key = "test-actor-123";

        // Act
        var hash1 = SimdHashHelper.ComputeFastHash(key);
        var hash2 = SimdHashHelper.ComputeFastHash(key);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    /// <summary>
    /// Tests that different inputs produce different hashes.
    /// Ensures good hash distribution.
    /// </summary>
    [Fact]
    public void ComputeFastHash_DifferentInputs_ProduceDifferentHashes()
    {
        // Arrange
        var key1 = "actor-1";
        var key2 = "actor-2";

        // Act
        var hash1 = SimdHashHelper.ComputeFastHash(key1);
        var hash2 = SimdHashHelper.ComputeFastHash(key2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    /// <summary>
    /// Tests hashing of empty string doesn't throw.
    /// Edge case handling.
    /// </summary>
    [Fact]
    public void ComputeFastHash_EmptyString_ReturnsZero()
    {
        // Act
        var hash = SimdHashHelper.ComputeFastHash(string.Empty);

        // Assert
        Assert.Equal(0u, hash);
    }

    /// <summary>
    /// Tests hashing of small strings (< 256 bytes) that use stack allocation.
    /// This is the common case for actor IDs.
    /// </summary>
    [Fact]
    public void ComputeFastHash_SmallString_ProducesValidHash()
    {
        // Arrange
        var key = "small-actor-id";

        // Act
        var hash = SimdHashHelper.ComputeFastHash(key);

        // Assert
        Assert.NotEqual(0u, hash);
    }

    /// <summary>
    /// Tests hashing of large strings (> 256 bytes) that use ArrayPool.
    /// Ensures no issues with larger actor IDs or keys.
    /// </summary>
    [Fact]
    public void ComputeFastHash_LargeString_ProducesValidHash()
    {
        // Arrange - Create a string larger than 256 bytes
        var key = new string('x', 300);

        // Act
        var hash = SimdHashHelper.ComputeFastHash(key);

        // Assert
        Assert.NotEqual(0u, hash);
    }

    /// <summary>
    /// Tests ComputeCompositeKeyHash produces consistent results.
    /// This is used for actor placement without string allocation.
    /// </summary>
    [Fact]
    public void ComputeCompositeKeyHash_SameInputs_ProducesSameHash()
    {
        // Arrange
        var actorType = "CounterActor";
        var actorId = "counter-123";

        // Act
        var hash1 = SimdHashHelper.ComputeCompositeKeyHash(actorType, actorId);
        var hash2 = SimdHashHelper.ComputeCompositeKeyHash(actorType, actorId);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    /// <summary>
    /// Tests that composite key hash matches concatenated string hash.
    /// Ensures compatibility with string-based lookups.
    /// </summary>
    [Fact]
    public void ComputeCompositeKeyHash_MatchesConcatenatedString()
    {
        // Arrange
        var actorType = "WorkerActor";
        var actorId = "worker-456";
        var concatenated = $"{actorType}:{actorId}";

        // Act
        var compositeHash = SimdHashHelper.ComputeCompositeKeyHash(actorType, actorId);
        var stringHash = SimdHashHelper.ComputeFastHash(concatenated);

        // Assert - Hashes should be identical
        Assert.Equal(compositeHash, stringHash);
    }

    /// <summary>
    /// Tests that composite hash differentiates between different actor types.
    /// Important for placement diversity.
    /// </summary>
    [Fact]
    public void ComputeCompositeKeyHash_DifferentTypes_ProduceDifferentHashes()
    {
        // Arrange
        var actorId = "actor-789";

        // Act
        var hash1 = SimdHashHelper.ComputeCompositeKeyHash("TypeA", actorId);
        var hash2 = SimdHashHelper.ComputeCompositeKeyHash("TypeB", actorId);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    /// <summary>
    /// Tests that composite hash differentiates between different actor IDs.
    /// Important for placement diversity.
    /// </summary>
    [Fact]
    public void ComputeCompositeKeyHash_DifferentIds_ProduceDifferentHashes()
    {
        // Arrange
        var actorType = "OrderActor";

        // Act
        var hash1 = SimdHashHelper.ComputeCompositeKeyHash(actorType, "order-1");
        var hash2 = SimdHashHelper.ComputeCompositeKeyHash(actorType, "order-2");

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    /// <summary>
    /// Tests hash distribution for consistent hashing.
    /// Good distribution is critical for load balancing.
    /// </summary>
    [Fact]
    public void ComputeFastHash_ManyKeys_HasGoodDistribution()
    {
        // Arrange
        var hashes = new HashSet<uint>();
        const int keyCount = 1000;

        // Act - Generate hashes for many keys
        for (int i = 0; i < keyCount; i++)
        {
            var key = $"actor-{i}";
            var hash = SimdHashHelper.ComputeFastHash(key);
            hashes.Add(hash);
        }

        // Assert - At least 99% unique hashes (allowing for tiny collision rate)
        var uniquePercentage = (double)hashes.Count / keyCount * 100;
        Assert.True(uniquePercentage >= 99.0, 
            $"Hash distribution poor: only {uniquePercentage:F2}% unique hashes");
    }

    /// <summary>
    /// Tests that hash computation doesn't allocate for typical actor IDs.
    /// This is a performance-critical property for hot paths.
    /// </summary>
    [Fact]
    public void ComputeFastHash_TypicalActorId_MinimalAllocations()
    {
        // Arrange
        var key = "CounterActor:counter-42";

        // Act - Warm up
        SimdHashHelper.ComputeFastHash(key);

        // Measure allocations
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < 100; i++)
        {
            SimdHashHelper.ComputeFastHash(key);
        }
        var afterBytes = GC.GetTotalAllocatedBytes(precise: true);

        var bytesAllocated = afterBytes - beforeBytes;

        // Assert - Should have minimal allocations (< 100 bytes per hash)
        // Note: Some allocation may occur due to GC internals, but should be minimal
        var avgBytesPerHash = bytesAllocated / 100.0;
        Assert.True(avgBytesPerHash < 100, 
            $"Too many allocations: {avgBytesPerHash:F2} bytes per hash");
    }

    /// <summary>
    /// Tests Unicode string handling.
    /// Ensures UTF-8 encoding works correctly for international actor IDs.
    /// </summary>
    [Fact]
    public void ComputeFastHash_UnicodeString_ProducesValidHash()
    {
        // Arrange
        var key = "актор-🚀-中文";

        // Act
        var hash = SimdHashHelper.ComputeFastHash(key);

        // Assert
        Assert.NotEqual(0u, hash);
    }
}
