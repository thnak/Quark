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
    /// This name is used for actor type registration and remote invocation.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the interface type that this actor implements for remote calls.
    /// If specified, the actor will be registered under the fully qualified interface name,
    /// allowing clients to call it via ActorProxyFactory.CreateProxy&lt;TInterface&gt;().
    /// </summary>
    /// <remarks>
    /// Example: [Actor(InterfaceType = typeof(ICounterActor))]
    /// This registers the actor under "MyNamespace.ICounterActor" instead of the class name.
    /// </remarks>
    public Type? InterfaceType { get; set; }

    /// <summary>
    /// Gets or sets whether this actor supports reentrancy.
    /// If true, the actor can process messages while another message is being processed.
    /// Default is false for safety.
    /// </summary>
    public bool Reentrant { get; set; }

    /// <summary>
    /// Gets or sets whether this is a stateless worker actor.
    /// If true, multiple instances can be created for the same actor ID for load balancing.
    /// Stateless actors have no state persistence overhead.
    /// Default is false.
    /// </summary>
    public bool Stateless { get; set; }
}
