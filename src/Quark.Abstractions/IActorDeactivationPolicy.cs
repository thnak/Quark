// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions;

/// <summary>
/// Defines a policy for determining when actors should be deactivated.
/// Used by serverless actor hosting to implement auto-scaling from zero.
/// </summary>
public interface IActorDeactivationPolicy
{
    /// <summary>
    /// Determines if an actor should be deactivated based on its activity.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="lastActivityTime">The time of the actor's last activity.</param>
    /// <param name="currentQueueDepth">The current message queue depth.</param>
    /// <param name="activeCallCount">The number of active calls in progress.</param>
    /// <returns>True if the actor should be deactivated, false otherwise.</returns>
    bool ShouldDeactivate(
        string actorId, 
        string actorType, 
        DateTimeOffset lastActivityTime, 
        int currentQueueDepth, 
        int activeCallCount);
}
