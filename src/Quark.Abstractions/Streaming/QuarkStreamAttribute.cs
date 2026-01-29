// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Marks an actor class to automatically subscribe to a stream namespace.
/// When a message is published to the specified namespace, the actor will be activated and receive the message.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class QuarkStreamAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkStreamAttribute"/> class.
    /// </summary>
    /// <param name="namespace">The stream namespace to subscribe to (e.g., "orders/processed").</param>
    public QuarkStreamAttribute(string @namespace)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Stream namespace cannot be null or empty.", nameof(@namespace));
        
        Namespace = @namespace;
    }

    /// <summary>
    /// Gets the stream namespace this actor subscribes to.
    /// </summary>
    public string Namespace { get; }
}
