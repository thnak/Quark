using System.Text.Json.Serialization;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Migration;

namespace Quark.Clustering.Redis;

/// <summary>
///     JSON serializer context for Quark clustering types.
///     Uses source generation for AOT compatibility (zero reflection).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SiloInfo))]
[JsonSerializable(typeof(SiloHealthScore))]
[JsonSerializable(typeof(AssemblyVersionInfo))]
[JsonSerializable(typeof(Dictionary<string, AssemblyVersionInfo>))]
internal partial class QuarkJsonSerializerContext : JsonSerializerContext
{
}