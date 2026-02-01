using System.Text.Json.Serialization;

namespace Quark.AwesomePizza.Shared.Constants;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.GpsLocation))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.ChefState))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.DriverState))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.CreateOrderRequest))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.CreateOrderResponse))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.DriverState))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.InventoryItem))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.InventoryState))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.KitchenState))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.KitchenQueueItem))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.OrderState))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.PizzaItem))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.UpdateDriverLocationRequest))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.UpdateStatusRequest))]
[JsonSerializable(typeof(Quark.AwesomePizza.Shared.Models.RestaurantMetrics))]
public partial class ModelJsonContext : JsonSerializerContext;