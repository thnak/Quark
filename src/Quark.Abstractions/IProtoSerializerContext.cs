using System;
using System.Collections.Generic;

namespace Quark.Abstractions;

/// <summary>
/// Interface for ProtoSerializer contexts that define types for ProtoBuf serialization.
/// Implementations of this interface provide metadata about serializable types and custom converters.
/// </summary>
public interface IProtoSerializerContext
{
    /// <summary>
    /// Gets the collection of types that are registered in this context.
    /// </summary>
    IReadOnlyCollection<Type> RegisteredTypes { get; }

    /// <summary>
    /// Gets the collection of custom converters registered in this context.
    /// </summary>
    IReadOnlyDictionary<Type, Type> CustomConverters { get; }

    /// <summary>
    /// Gets the name of this serializer context.
    /// </summary>
    string ContextName { get; }
}
