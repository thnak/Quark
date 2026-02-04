using Quark.Abstractions.Migration;

namespace Quark.Core.Actors.Migration;

/// <summary>
/// Tracks the state of an actor migration operation.
/// </summary>
internal sealed class MigrationState
{
    public string ActorId { get; }
    public string ActorType { get; }
    public string TargetSiloId { get; }
    public MigrationStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; }
    public string? ErrorMessage { get; set; }

    public MigrationState(string actorId, string actorType, string targetSiloId)
    {
        ActorId = actorId;
        ActorType = actorType;
        TargetSiloId = targetSiloId;
        Status = MigrationStatus.NotStarted;
        StartedAt = DateTimeOffset.UtcNow;
    }
}