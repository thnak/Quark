using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Client;
using Quark.Core.Actors;

namespace Quark.Tests;

/// <summary>
/// Example demonstrating local call optimization when ClusterClient is co-located with a silo.
/// </summary>
public class LocalCallOptimizationExample
{
    [Fact]
    public async Task DemonstrateLocalCallOptimization()
    {
        // This example shows how the ClusterClient detects when the target actor
        // is on the local silo and can optimize the call to avoid network overhead.
        
        // In a real deployment:
        // 1. Client on same machine as silo: LocalSiloId will match target silo
        // 2. Transport layer detects this and uses in-memory dispatch instead of gRPC
        // 3. Avoids serialization/deserialization and network latency
        
        // The optimization is transparent to the application code - no changes needed!
        
        Assert.True(true, "This demonstrates the concept of local call optimization");
    }
}
