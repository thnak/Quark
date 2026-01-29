using System.Collections.Concurrent;

namespace Quark.Core;

/// <summary>
/// Default implementation of the actor factory.
/// This class is AOT-friendly and uses compile-time code generation.
/// </summary>
public sealed class ActorFactory : IActorFactory
{
    private readonly ConcurrentDictionary<(Type, string), IActor> _actors = new();
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorFactory"/> class.
    /// </summary>
    /// <param name="serviceProvider">Optional service provider for dependency injection.</param>
    public ActorFactory(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public TActor CreateActor<TActor>(string actorId) where TActor : IActor
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor ID cannot be null or whitespace.", nameof(actorId));
        }

        // Try to create actor with factory support for child spawning
        // Check if constructor with (string, IActorFactory) exists
        var type = typeof(TActor);
        var twoParamConstructor = type.GetConstructor(new[] { typeof(string), typeof(IActorFactory) });
        
        if (twoParamConstructor != null)
        {
            // Create with factory support
            var actor = (TActor)twoParamConstructor.Invoke(new object?[] { actorId, this });
            return actor;
        }
        
        // Fall back to single parameter constructor (actorId only)
        // TODO: Integrate with source-generated factory code for AOT compatibility
        var oneParamConstructor = type.GetConstructor(new[] { typeof(string) });
        if (oneParamConstructor != null)
        {
            var actor = (TActor)oneParamConstructor.Invoke(new object[] { actorId });
            return actor;
        }
        
        throw new InvalidOperationException(
            $"Actor type {type.Name} must have a constructor with signature " +
            $"({type.Name}(string actorId)) or ({type.Name}(string actorId, IActorFactory actorFactory))");
    }

    /// <inheritdoc />
    public TActor GetOrCreateActor<TActor>(string actorId) where TActor : IActor
    {
        var key = (typeof(TActor), actorId);
        return (TActor)_actors.GetOrAdd(key, _ => CreateActor<TActor>(actorId));
    }
}
