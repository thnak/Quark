namespace Quark.Abstractions;

/// <summary>
///     Phase 8.3: Options for adaptive mailbox sizing to handle traffic bursts.
/// </summary>
public sealed class AdaptiveMailboxOptions
{
    /// <summary>
    ///     Gets or sets the initial mailbox capacity.
    ///     Default is 1000.
    /// </summary>
    public int InitialCapacity { get; set; } = 1000;

    /// <summary>
    ///     Gets or sets the minimum mailbox capacity.
    ///     The mailbox will never shrink below this size.
    ///     Default is 100.
    /// </summary>
    public int MinCapacity { get; set; } = 100;

    /// <summary>
    ///     Gets or sets the maximum mailbox capacity.
    ///     The mailbox will never grow beyond this size.
    ///     Default is 10000.
    /// </summary>
    public int MaxCapacity { get; set; } = 10000;

    /// <summary>
    ///     Gets or sets the threshold (percentage) at which to grow the mailbox.
    ///     When the mailbox is this full, capacity will be increased.
    ///     Default is 0.8 (80%).
    /// </summary>
    public double GrowThreshold { get; set; } = 0.8;

    /// <summary>
    ///     Gets or sets the threshold (percentage) at which to shrink the mailbox.
    ///     When the mailbox is consistently this empty, capacity will be decreased.
    ///     Default is 0.2 (20%).
    /// </summary>
    public double ShrinkThreshold { get; set; } = 0.2;

    /// <summary>
    ///     Gets or sets the growth factor when expanding capacity.
    ///     Default is 2.0 (double the capacity).
    /// </summary>
    public double GrowthFactor { get; set; } = 2.0;

    /// <summary>
    ///     Gets or sets the shrink factor when reducing capacity.
    ///     Default is 0.5 (halve the capacity).
    /// </summary>
    public double ShrinkFactor { get; set; } = 0.5;

    /// <summary>
    ///     Gets or sets the minimum number of samples required before adapting capacity.
    ///     This prevents rapid oscillation.
    ///     Default is 10 samples.
    /// </summary>
    public int MinSamplesBeforeAdapt { get; set; } = 10;

    /// <summary>
    ///     Gets or sets whether adaptive sizing is enabled.
    ///     Default is false (disabled by default for backward compatibility).
    /// </summary>
    public bool Enabled { get; set; } = false;
}