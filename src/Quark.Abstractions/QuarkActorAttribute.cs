namespace Quark.Abstractions;

/// <summary>
/// Registers an actor interface for proxy generation within a QuarkActorContext.
/// The specified type will have Protobuf message contracts and client proxies generated.
/// </summary>
/// <remarks>
/// This attribute can be applied multiple times to register multiple actor interfaces.
/// The registered type must be an interface with async methods (Task or Task&lt;T&gt; return types).
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
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class QuarkActorAttribute : Attribute
{
    /// <summary>
    /// Gets the actor interface type to register for proxy generation.
    /// </summary>
    public Type ActorType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkActorAttribute"/> class.
    /// </summary>
    /// <param name="actorType">The actor interface type to register.</param>
    public QuarkActorAttribute(Type actorType)
    {
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
    }
}
