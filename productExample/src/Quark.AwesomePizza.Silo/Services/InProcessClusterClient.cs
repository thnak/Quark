using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.AwesomePizza.Shared.Client;

namespace Quark.AwesomePizza.Silo.Services;

/// <summary>
/// In-process implementation of IClusterClient for Silo.
/// This provides actor access within the same process until full distributed IClusterClient is available.
/// </summary>
public class InProcessClusterClient : IClusterClient
{
    private readonly IActorFactory _actorFactory;
    private readonly Dictionary<string, IActor> _activeActors = new();
    private bool _isConnected;

    public InProcessClusterClient(IActorFactory actorFactory)
    {
        ArgumentNullException.ThrowIfNull(actorFactory);
        _actorFactory = actorFactory;
    }

    public bool IsConnected => _isConnected;

    public T GetActor<T>(string actorId) where T : class
    {
        ArgumentNullException.ThrowIfNull(actorId);

        if (!_isConnected)
            throw new InvalidOperationException("Client is not connected. Call ConnectAsync() first.");

        if (_activeActors.TryGetValue(actorId, out var existingActor) && existingActor is T typedActor)
        {
            return typedActor;
        }

        // Create actor using factory - must be IActor
        var actor = _actorFactory.CreateActor<IActor>(actorId);
        actor.OnActivateAsync().GetAwaiter().GetResult();
        _activeActors[actorId] = actor;

        // Return as requested type (will be the concrete actor type)
        if (actor is T result)
            return result;

        throw new InvalidOperationException($"Actor {actorId} is not of type {typeof(T).Name}");
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _isConnected = false;
        
        // Deactivate all actors
        foreach (var actor in _activeActors.Values)
        {
            try
            {
                actor.OnDeactivateAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Log but continue
            }
        }
        
        _activeActors.Clear();
        return Task.CompletedTask;
    }
}
