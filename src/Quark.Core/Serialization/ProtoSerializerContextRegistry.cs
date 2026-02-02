using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Quark.Abstractions;

namespace Quark.Core.Serialization;

/// <summary>
/// Registry for ProtoSerializer contexts.
/// Manages the registration and discovery of types and custom converters
/// defined in ProtoSerializer contexts.
/// </summary>
public sealed class ProtoSerializerContextRegistry
{
    private static readonly Lazy<ProtoSerializerContextRegistry> _instance =
        new(() => new ProtoSerializerContextRegistry());

    private readonly ConcurrentDictionary<string, IProtoSerializerContext> _contexts = new();
    private readonly ConcurrentDictionary<Type, Type> _customConverters = new();
    private readonly ConcurrentDictionary<Type, bool> _registeredTypes = new();

    /// <summary>
    /// Gets the singleton instance of the registry.
    /// </summary>
    public static ProtoSerializerContextRegistry Instance => _instance.Value;

    private ProtoSerializerContextRegistry()
    {
    }

    /// <summary>
    /// Registers a ProtoSerializer context.
    /// </summary>
    /// <param name="context">The context to register.</param>
    public void RegisterContext(IProtoSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_contexts.TryAdd(context.ContextName, context))
        {
            // Register all types from the context
            foreach (var type in context.RegisteredTypes)
            {
                _registeredTypes.TryAdd(type, true);
            }

            // Register custom converters
            foreach (var kvp in context.CustomConverters)
            {
                _customConverters.TryAdd(kvp.Key, kvp.Value);
            }
        }
    }

    /// <summary>
    /// Auto-discovers and registers all ProtoSerializer contexts in the specified assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    public void AutoRegisterContexts(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            AutoRegisterContexts(assembly);
        }
    }

    /// <summary>
    /// Auto-discovers and registers all ProtoSerializer contexts in the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    public void AutoRegisterContexts(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var contextTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<ProtoSerializerContextAttribute>() != null)
            .Where(t => typeof(IProtoSerializerContext).IsAssignableFrom(t))
            .Where(t => !t.IsAbstract && !t.IsInterface);

        foreach (var contextType in contextTypes)
        {
            // Try to get singleton instance property
            var instanceProperty = contextType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);

            if (instanceProperty != null && instanceProperty.PropertyType == contextType)
            {
                var instance = instanceProperty.GetValue(null) as IProtoSerializerContext;
                if (instance != null)
                {
                    RegisterContext(instance);
                    continue;
                }
            }

            // Fallback: try to create an instance
            if (contextType.GetConstructor(Type.EmptyTypes) != null)
            {
                var instance = Activator.CreateInstance(contextType) as IProtoSerializerContext;
                if (instance != null)
                {
                    RegisterContext(instance);
                }
            }
        }
    }

    /// <summary>
    /// Checks if a type is registered in any context.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is registered; otherwise, false.</returns>
    public bool IsTypeRegistered(Type type)
    {
        return _registeredTypes.ContainsKey(type);
    }

    /// <summary>
    /// Gets the custom converter type for the specified type, if any.
    /// </summary>
    /// <param name="type">The type to get the converter for.</param>
    /// <returns>The converter type, or null if no custom converter is registered.</returns>
    public Type? GetCustomConverterType(Type type)
    {
        _customConverters.TryGetValue(type, out var converterType);
        return converterType;
    }

    /// <summary>
    /// Gets all registered types.
    /// </summary>
    public IEnumerable<Type> RegisteredTypes => _registeredTypes.Keys;

    /// <summary>
    /// Gets all registered contexts.
    /// </summary>
    public IEnumerable<IProtoSerializerContext> Contexts => _contexts.Values;

    /// <summary>
    /// Clears all registered contexts (primarily for testing).
    /// </summary>
    public void Clear()
    {
        _contexts.Clear();
        _customConverters.Clear();
        _registeredTypes.Clear();
    }
}
