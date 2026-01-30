namespace Quark.Placement.Abstractions;

/// <summary>
/// Marks an actor as requiring GPU acceleration.
/// The source generator will automatically collect all actors with this attribute
/// and generate a static list for easy configuration.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [Actor]
/// [GpuBound]
/// public class InferenceActor : ActorBase
/// {
///     // Actor implementation
/// }
/// 
/// // In configuration:
/// options.AcceleratedActorTypes = MyAssemblyAcceleratedActorTypes.All;
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GpuBoundAttribute : Attribute
{
}
