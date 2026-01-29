using System.Text.Json;
using System.Text.Json.Serialization;
using Quark.Abstractions.Clustering;

namespace Quark.Clustering.Redis;

/// <summary>
/// JSON serializer context for Quark clustering types.
/// Uses source generation for AOT compatibility (zero reflection).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SiloInfo))]
internal partial class QuarkJsonSerializerContext : JsonSerializerContext
{
}
