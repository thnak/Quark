using System.Text.Json.Serialization;

namespace Quark.Reminders.Redis;

/// <summary>Source-generated JSON serializer context for <see cref="RedisReminderStorage.ReminderEntryDto" />.</summary>
[JsonSerializable(typeof(RedisReminderStorage.ReminderEntryDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ReminderEntryDtoJsonContext : JsonSerializerContext
{
}