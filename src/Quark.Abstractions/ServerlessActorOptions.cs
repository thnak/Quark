// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions;

/// <summary>
/// Configuration options for serverless actor hosting with auto-scaling from zero.
/// </summary>
public sealed class ServerlessActorOptions
{
    /// <summary>
    /// Gets or sets the idle timeout before an actor is automatically deactivated.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the interval at which the idle deactivation service checks for idle actors.
    /// Default is 1 minute.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets whether serverless auto-deactivation is enabled.
    /// Default is false (opt-in).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum number of actors to keep active even if idle.
    /// This provides a warm pool for faster responses.
    /// Default is 0 (scale to zero).
    /// </summary>
    public int MinimumActiveActors { get; set; } = 0;

    /// <summary>
    /// Gets or sets whether to eagerly load state during activation for faster first request.
    /// Default is false (lazy loading).
    /// </summary>
    public bool EagerStateLoading { get; set; } = false;
}
