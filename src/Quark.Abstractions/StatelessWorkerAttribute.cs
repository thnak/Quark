// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions;

/// <summary>
/// Configures scaling behavior for stateless worker actors.
/// This attribute is used to define min/max instance counts for load balancing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StatelessWorkerAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the minimum number of instances to maintain.
    /// Default is 1.
    /// </summary>
    public int MinInstances { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum number of instances allowed.
    /// Default is 10.
    /// </summary>
    public int MaxInstances { get; set; } = 10;
}
