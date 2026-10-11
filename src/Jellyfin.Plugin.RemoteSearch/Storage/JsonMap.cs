using System.Text.Json;

namespace Jellyfin.Plugin.RemoteSearch.Storage;

/// <summary>Thread-safe string-keyed map persisted as one JSON file, rewritten atomically on every change.</summary>
internal sealed class JsonMap<TValue>
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, TValue> _entries;

    public JsonMap(string filePath)
    {
        _filePath = filePath;
        _entries = Load(filePath);
    }

    public TValue? Get(string key)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(key);
        }
    }

    public IReadOnlyList<TValue> Values
    {
        get
        {
            lock (_lock)
            {
                return _entries.Values.ToList();
            }
        }
    }

    public void Set(string key, TValue value)
    {
        lock (_lock)
        {
            _entries[key] = value;
            Save();
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            if (_entries.Remove(key))
            {
                Save();
            }
        }
    }

    private static Dictionary<string, TValue> Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<Dictionary<string, TValue>>(json, SerializerOptions) ?? [];
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_entries, SerializerOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
