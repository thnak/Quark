namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Declares the stable string ID that maps this behavior class to a grain interface.
///     Applied by the code generator; can also be applied manually on hand-authored behaviors.
///     The ID must be stable across deployments — it is used as the grain type key.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GrainBehaviorAttribute(string behaviorId) : Attribute
{
    public string BehaviorId { get; } = behaviorId;
}
