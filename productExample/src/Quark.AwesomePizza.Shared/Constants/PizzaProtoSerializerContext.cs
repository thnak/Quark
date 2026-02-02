using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Constants;

/// <summary>
/// ProtoSerializer context for the Awesome Pizza application.
/// This context defines all the types that should be serializable via ProtoBuf
/// for distributed actor calls.
/// </summary>
/// <remarks>
/// This follows a similar pattern to System.Text.Json's JsonSerializerContext.
/// All types used in actor interfaces should be registered here or have [ProtoContract] attribute.
/// </remarks>
[ProtoSerializerContext(Name = "AwesomePizza")]
[ProtoInclude(typeof(CreateOrderRequest))]
[ProtoInclude(typeof(CreateOrderResponse))]
[ProtoInclude(typeof(OrderState))]
[ProtoInclude(typeof(OrderStatusUpdate))]
[ProtoInclude(typeof(UpdateStatusRequest))]
[ProtoInclude(typeof(GpsLocation))]
[ProtoInclude(typeof(PizzaItem))]
[ProtoInclude(typeof(ChefState))]
[ProtoInclude(typeof(DriverState))]
[ProtoInclude(typeof(KitchenState))]
[ProtoInclude(typeof(KitchenQueueItem))]
[ProtoInclude(typeof(InventoryState))]
[ProtoInclude(typeof(InventoryItem))]
[ProtoInclude(typeof(RestaurantMetrics))]
[ProtoInclude(typeof(UpdateDriverLocationRequest))]
public sealed partial class PizzaProtoSerializerContext : IProtoSerializerContext
{
    private static readonly Type[] _registeredTypes = new[]
    {
        typeof(CreateOrderRequest),
        typeof(CreateOrderResponse),
        typeof(OrderState),
        typeof(OrderStatusUpdate),
        typeof(UpdateStatusRequest),
        typeof(GpsLocation),
        typeof(PizzaItem),
        typeof(ChefState),
        typeof(DriverState),
        typeof(KitchenState),
        typeof(KitchenQueueItem),
        typeof(InventoryState),
        typeof(InventoryItem),
        typeof(RestaurantMetrics),
        typeof(UpdateDriverLocationRequest)
    };

    private static readonly Dictionary<Type, Type> _customConverters = new();

    /// <inheritdoc />
    public IReadOnlyCollection<Type> RegisteredTypes => _registeredTypes;

    /// <inheritdoc />
    public IReadOnlyDictionary<Type, Type> CustomConverters => _customConverters;

    /// <inheritdoc />
    public string ContextName => "AwesomePizza";

    /// <summary>
    /// Gets the singleton instance of the Pizza ProtoSerializer context.
    /// </summary>
    public static PizzaProtoSerializerContext Instance { get; } = new();

    private PizzaProtoSerializerContext()
    {
    }
}
