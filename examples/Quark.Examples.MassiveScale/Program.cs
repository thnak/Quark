using Quark.Abstractions;
using Quark.Abstractions.Clustering;
using Quark.Core.Actors;
using Quark.Networking.Abstractions;

namespace Quark.Examples.MassiveScale;

/// <summary>
///     Phase 8.3: Example demonstrating massive scale support features.
///     
///     This example showcases:
///     1. Hierarchical consistent hashing for geo-aware placement
///     2. Adaptive mailbox sizing for burst handling
///     3. Circuit breakers for fault tolerance
///     4. Rate limiting for traffic control
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Quark Phase 8.3: Massive Scale Support Example ===\n");

        // Demonstrate hierarchical hash ring
        DemonstrateHierarchicalHashing();
        
        Console.WriteLine();
        
        // Demonstrate adaptive mailbox
        DemonstrateAdaptiveMailbox();
        
        Console.WriteLine();
        
        // Demonstrate circuit breaker
        DemonstrateCircuitBreaker();
        
        Console.WriteLine();
        
        // Demonstrate rate limiting
        DemonstrateRateLimiting();

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static void DemonstrateHierarchicalHashing()
    {
        Console.WriteLine("--- 1. Hierarchical Consistent Hashing ---");
        Console.WriteLine("Organizing a global cluster with multiple regions and zones.\n");

        // Create a hierarchical hash ring
        var ring = new HierarchicalHashRing(new GeoRoutingOptions
        {
            PreferSameRegion = true,
            PreferSameZone = true
        });

        // Add silos across multiple regions and zones
        Console.WriteLine("Adding silos to the cluster:");
        
        // US East region
        ring.AddNode(new HierarchicalHashRingNode("us-east-1a-silo-1", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("us-east-1a-silo-2", "us-east", "us-east-1a"));
        ring.AddNode(new HierarchicalHashRingNode("us-east-1b-silo-1", "us-east", "us-east-1b"));
        Console.WriteLine("  ✓ US East: 3 silos (2 in zone 1a, 1 in zone 1b)");

        // US West region
        ring.AddNode(new HierarchicalHashRingNode("us-west-2a-silo-1", "us-west", "us-west-2a"));
        ring.AddNode(new HierarchicalHashRingNode("us-west-2b-silo-1", "us-west", "us-west-2b"));
        Console.WriteLine("  ✓ US West: 2 silos (1 in zone 2a, 1 in zone 2b)");

        // EU West region
        ring.AddNode(new HierarchicalHashRingNode("eu-west-1a-silo-1", "eu-west", "eu-west-1a"));
        ring.AddNode(new HierarchicalHashRingNode("eu-west-1a-silo-2", "eu-west", "eu-west-1a"));
        Console.WriteLine("  ✓ EU West: 2 silos (2 in zone 1a)");

        Console.WriteLine($"\nCluster Statistics:");
        Console.WriteLine($"  Total Silos: {ring.TotalNodeCount}");
        Console.WriteLine($"  Regions: {ring.RegionCount}");
        Console.WriteLine($"  Zones: {ring.ZoneCount}");

        // Demonstrate geo-aware placement
        Console.WriteLine("\nGeo-Aware Actor Placement:");
        
        var actor1 = ring.GetNode("user-12345", preferredRegionId: "us-east");
        Console.WriteLine($"  Actor 'user-12345' (prefer US East) → {actor1}");

        var actor2 = ring.GetNode("user-67890", preferredRegionId: "eu-west");
        Console.WriteLine($"  Actor 'user-67890' (prefer EU West) → {actor2}");

        var actor3 = ring.GetNode("user-11111", preferredRegionId: "us-east", preferredZoneId: "us-east-1a");
        Console.WriteLine($"  Actor 'user-11111' (prefer US East, zone 1a) → {actor3}");
    }

    static void DemonstrateAdaptiveMailbox()
    {
        Console.WriteLine("--- 2. Adaptive Mailbox Sizing ---");
        Console.WriteLine("Dynamically adjusting mailbox capacity based on load.\n");

        var options = new AdaptiveMailboxOptions
        {
            Enabled = true,
            InitialCapacity = 100,
            MinCapacity = 50,
            MaxCapacity = 1000,
            GrowThreshold = 0.8,  // Grow when 80% full
            ShrinkThreshold = 0.2, // Shrink when 20% full
            GrowthFactor = 2.0,    // Double capacity
            ShrinkFactor = 0.5     // Halve capacity
        };

        Console.WriteLine("Adaptive Mailbox Configuration:");
        Console.WriteLine($"  Initial Capacity: {options.InitialCapacity}");
        Console.WriteLine($"  Min Capacity: {options.MinCapacity}");
        Console.WriteLine($"  Max Capacity: {options.MaxCapacity}");
        Console.WriteLine($"  Grow Threshold: {options.GrowThreshold * 100}%");
        Console.WriteLine($"  Shrink Threshold: {options.ShrinkThreshold * 100}%");
        Console.WriteLine("\nUnder burst load:");
        Console.WriteLine("  • Mailbox detects 80%+ utilization");
        Console.WriteLine("  • Capacity automatically doubles: 100 → 200 → 400 → 800");
        Console.WriteLine("  • Prevents message drops during traffic spikes");
        Console.WriteLine("\nDuring low load:");
        Console.WriteLine("  • Mailbox detects <20% utilization");
        Console.WriteLine("  • Capacity automatically halves: 800 → 400 → 200 → 100");
        Console.WriteLine("  • Reduces memory footprint when idle");
    }

    static void DemonstrateCircuitBreaker()
    {
        Console.WriteLine("--- 3. Circuit Breaker ---");
        Console.WriteLine("Protecting actors from cascading failures.\n");

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 5,      // Open after 5 failures
            SuccessThreshold = 3,       // Close after 3 successes
            Timeout = TimeSpan.FromSeconds(30)
        };

        Console.WriteLine("Circuit Breaker Configuration:");
        Console.WriteLine($"  Failure Threshold: {options.FailureThreshold} consecutive failures");
        Console.WriteLine($"  Success Threshold: {options.SuccessThreshold} consecutive successes");
        Console.WriteLine($"  Timeout: {options.Timeout.TotalSeconds} seconds");
        
        Console.WriteLine("\nCircuit States:");
        Console.WriteLine("  1. CLOSED (normal operation)");
        Console.WriteLine("     • All messages processed normally");
        Console.WriteLine("     • Failures are counted");
        Console.WriteLine($"     • Opens after {options.FailureThreshold} consecutive failures");
        
        Console.WriteLine("\n  2. OPEN (failures detected)");
        Console.WriteLine("     • All messages immediately rejected");
        Console.WriteLine("     • Prevents cascading failures");
        Console.WriteLine($"     • Transitions to HALF-OPEN after {options.Timeout.TotalSeconds}s");
        
        Console.WriteLine("\n  3. HALF-OPEN (testing recovery)");
        Console.WriteLine("     • Limited messages allowed through");
        Console.WriteLine($"     • Closes after {options.SuccessThreshold} successes");
        Console.WriteLine("     • Reopens immediately on any failure");
    }

    static void DemonstrateRateLimiting()
    {
        Console.WriteLine("--- 4. Rate Limiting ---");
        Console.WriteLine("Controlling message throughput per actor.\n");

        var options = new RateLimitOptions
        {
            Enabled = true,
            MaxMessagesPerWindow = 1000,
            TimeWindow = TimeSpan.FromSeconds(1),
            ExcessAction = RateLimitAction.Drop
        };

        Console.WriteLine("Rate Limiter Configuration:");
        Console.WriteLine($"  Limit: {options.MaxMessagesPerWindow} messages per {options.TimeWindow.TotalSeconds} second");
        Console.WriteLine($"  Excess Action: {options.ExcessAction}");

        Console.WriteLine("\nRate Limit Actions:");
        Console.WriteLine("  • DROP: Silently discard excess messages");
        Console.WriteLine("    - Good for non-critical messages");
        Console.WriteLine("    - Protects actor from overload");
        
        Console.WriteLine("\n  • REJECT: Throw exception for excess messages");
        Console.WriteLine("    - Sender is notified of rejection");
        Console.WriteLine("    - Allows sender to retry with backoff");
        
        Console.WriteLine("\n  • QUEUE: Buffer excess messages for later");
        Console.WriteLine("    - Messages preserved during burst");
        Console.WriteLine("    - Subject to mailbox capacity limits");

        Console.WriteLine("\nExample Scenario:");
        Console.WriteLine("  Client sends 1500 messages in 1 second");
        Console.WriteLine($"  → First 1000 messages: Accepted and processed");
        Console.WriteLine($"  → Remaining 500 messages: {options.ExcessAction} (per configuration)");
    }
}
