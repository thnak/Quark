using System.Text.Json;
using Realm.Common.Models;

namespace Realm.Content;

public sealed class RealmContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, MapContent> _maps;

    public RealmContentLoader()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        _maps = new Dictionary<string, MapContent>(StringComparer.Ordinal);

        if (!Directory.Exists(dataDir))
            return;

        foreach (string file in Directory.EnumerateFiles(dataDir, "*.json"))
        {
            string json = File.ReadAllText(file);
            MapContent? map = JsonSerializer.Deserialize<MapContent>(json, JsonOptions);
            if (map is not null && map.Id.Length > 0)
                _maps[map.Id] = map;
        }
    }

    public MapContent? GetMap(string id) =>
        _maps.TryGetValue(id, out MapContent? m) ? m : null;

    public IReadOnlyDictionary<string, MapContent> All => _maps;
}
