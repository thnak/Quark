// Copyright (c) Quark Framework. All rights reserved.

using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Serverless.Actors;

/// <summary>
/// Example serverless actor that auto-deactivates when idle.
/// Demonstrates pay-per-use pattern with automatic scaling from zero.
/// </summary>
[Actor(Name = "ServerlessWorker", Stateless = true)]
public class ServerlessWorkerActor : StatelessActorBase
{
    public ServerlessWorkerActor(string actorId, IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"ServerlessWorker {ActorId} ACTIVATED at {DateTimeOffset.UtcNow}");
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"ServerlessWorker {ActorId} DEACTIVATED at {DateTimeOffset.UtcNow}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a data item (simulates compute work).
    /// </summary>
    public async Task<string> ProcessDataAsync(string data)
    {
        Console.WriteLine($"Processing data: {data}");
        
        // Simulate some work
        await Task.Delay(100);
        
        return $"Processed: {data} (by {ActorId})";
    }

    /// <summary>
    /// Validates input data.
    /// </summary>
    public Task<bool> ValidateAsync(string input)
    {
        var isValid = !string.IsNullOrWhiteSpace(input);
        Console.WriteLine($"Validation result for '{input}': {isValid}");
        return Task.FromResult(isValid);
    }
}
