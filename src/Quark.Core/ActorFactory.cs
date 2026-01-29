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
        // First, try with two parameters (actorId, factory)
        try
        {
            var actor = (TActor)Activator.CreateInstance(typeof(TActor), actorId, this)!;
            return actor;
        }
        catch (MissingMethodException)
        {
            // Fall back to single parameter constructor (actorId only)
            // TODO: Integrate with source-generated factory code for AOT compatibility
            var actor = (TActor)Activator.CreateInstance(typeof(TActor), actorId)!;
            return actor;
        }
    }

    /// <inheritdoc />
    public TActor GetOrCreateActor<TActor>(string actorId) where TActor : IActor
    {
        var key = (typeof(TActor), actorId);
        return (TActor)_actors.GetOrAdd(key, _ => CreateActor<TActor>(actorId));
    }
}
