// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions;

/// <summary>
/// Marks a class as an actor that should be code-generated for AOT compatibility.
/// This attribute is used by the Quark source generator to create factory methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ActorAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of the actor type.
    /// If not specified, the class name is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets whether this actor supports reentrancy.
    /// If true, the actor can process messages while another message is being processed.
    /// Default is false for safety.
    /// </summary>
    public bool Reentrant { get; set; }
}
