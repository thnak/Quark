using System.Text.Json.Serialization;
using Quark.Examples.PizzaTracker.Api.Endpoints;
using Quark.Examples.PizzaTracker.Shared.Models;

namespace Quark.Examples.PizzaTracker.Api;

/// <summary>
/// Source-generated JSON serializer context for AOT compatibility.
/// </summary>
[JsonSerializable(typeof(PizzaOrder))]
[JsonSerializable(typeof(PizzaStatusUpdate))]
[JsonSerializable(typeof(GpsLocation))]
[JsonSerializable(typeof(PizzaStatus))]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(CreateOrderResponse))]
[JsonSerializable(typeof(UpdateStatusRequest))]
[JsonSerializable(typeof(UpdateLocationRequest))]
[JsonSerializable(typeof(ApiInfoResponse))]
[JsonSerializable(typeof(EmptyRequest))]
public partial class PizzaTrackerJsonContext : JsonSerializerContext
{
}
