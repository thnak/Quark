namespace Quark.Abstractions;

/// <summary>
/// Marks a class as a context for registering actor interfaces for proxy generation.
/// Similar to System.Text.Json's JsonSerializerContext, this allows explicit registration
/// of actor interfaces for Protobuf message contract and proxy generation.
/// </summary>
/// <remarks>
/// This is useful when you need to generate proxies for types from external libraries
/// that cannot be modified to inherit from IQuarkActor, or when you want explicit
/// control over which types get proxy generation.
/// </remarks>
/// <example>
/// <code>
/// [QuarkActorContext]
/// [QuarkActor(typeof(ICounterActor))]
/// [QuarkActor(typeof(IUserActor))]
/// public partial class MyActorContext
/// {
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class QuarkActorContextAttribute : Attribute
{
}
