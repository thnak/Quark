// Copyright (c) Quark Framework. All rights reserved.

using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
/// Deactivation policy that deactivates actors after a configurable idle timeout.
/// Used for serverless actor hosting with auto-scaling from zero.
/// </summary>
public sealed class IdleTimeoutDeactivationPolicy : IActorDeactivationPolicy
{
    private readonly TimeSpan _idleTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdleTimeoutDeactivationPolicy"/> class.
    /// </summary>
    /// <param name="idleTimeout">The idle timeout after which actors should be deactivated.</param>
    public IdleTimeoutDeactivationPolicy(TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Idle timeout must be positive", nameof(idleTimeout));
        }

        _idleTimeout = idleTimeout;
    }

    /// <inheritdoc />
    public bool ShouldDeactivate(
        string actorId,
        string actorType,
        DateTimeOffset lastActivityTime,
        int currentQueueDepth,
        int activeCallCount)
    {
        // Never deactivate actors with pending work
        if (currentQueueDepth > 0 || activeCallCount > 0)
        {
            return false;
        }

        // Check if the actor has been idle longer than the timeout
        var idleDuration = DateTimeOffset.UtcNow - lastActivityTime;
        return idleDuration >= _idleTimeout;
    }
}
