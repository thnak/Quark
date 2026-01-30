namespace Quark.Abstractions.Clustering;

/// <summary>
/// Configuration options for actor rebalancing.
/// </summary>
/// <remarks>
/// Weight properties (StateSizeWeight, ActivationTimeWeight, MessageQueueWeight) should
/// sum to approximately 1.0 for balanced cost calculation, but can be any non-negative values.
/// The system normalizes the total cost to the range [0.0, 1.0] in the calculation.
/// </remarks>
public sealed class RebalancingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether rebalancing is enabled.
    /// Default is false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the interval between rebalancing evaluations.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the load imbalance threshold that triggers rebalancing.
    /// Value between 0.0 and 1.0. For example, 0.3 means rebalance if load difference exceeds 30%.
    /// Default is 0.3 (30% difference).
    /// </summary>
    public double LoadImbalanceThreshold { get; set; } = 0.3;

    /// <summary>
    /// Gets or sets the maximum migration cost allowed for a single rebalancing operation.
    /// Value between 0.0 and 1.0. Higher cost migrations will be skipped.
    /// Default is 0.7.
    /// </summary>
    public double MaxMigrationCost { get; set; } = 0.7;

    /// <summary>
    /// Gets or sets the maximum number of actors to migrate in a single rebalancing cycle.
    /// Default is 10.
    /// </summary>
    public int MaxMigrationsPerCycle { get; set; } = 10;

    /// <summary>
    /// Gets or sets the cooldown period after a migration before allowing another migration.
    /// This prevents rapid back-and-forth migrations.
    /// Default is 60 seconds.
    /// </summary>
    public TimeSpan MigrationCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the weight for state size in migration cost calculation (0.0 to 1.0).
    /// Default is 0.5 (50% weight).
    /// </summary>
    public double StateSizeWeight { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the weight for activation time in migration cost calculation (0.0 to 1.0).
    /// Default is 0.3 (30% weight).
    /// </summary>
    public double ActivationTimeWeight { get; set; } = 0.3;

    /// <summary>
    /// Gets or sets the weight for message queue depth in migration cost calculation (0.0 to 1.0).
    /// Default is 0.2 (20% weight).
    /// </summary>
    public double MessageQueueWeight { get; set; } = 0.2;
}
