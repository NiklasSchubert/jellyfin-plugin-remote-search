using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Backend;

namespace Jellyfin.Plugin.RemoteSearch.Storage;

public sealed record PlaceholderEntry(RemoteTrack Track, DateTime LastSeenUtc);

/// <summary>Search hits that exist as placeholder items, keyed by track id.</summary>
public sealed class PlaceholderStore
{
    private const string FileName = "placeholders.json";

    // every search touches its hits; only rewrite the file when the timestamp is meaningfully stale
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromHours(1);

    private readonly JsonMap<PlaceholderEntry> _entries;
    private readonly TimeProvider _timeProvider;

    public PlaceholderStore(PluginSettings settings, TimeProvider timeProvider)
        : this(Path.Combine(settings.StatePath, FileName), timeProvider)
    {
    }

    internal PlaceholderStore(string filePath, TimeProvider timeProvider)
    {
        _entries = new JsonMap<PlaceholderEntry>(filePath);
        _timeProvider = timeProvider;
    }

    public PlaceholderEntry? Get(string trackId) => _entries.Get(trackId);

    public IReadOnlyList<PlaceholderEntry> All => _entries.Values;

    public void Touch(RemoteTrack track)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = _entries.Get(track.Id);
        var isFresh = existing is not null && now - existing.LastSeenUtc < LastSeenResolution;
        if (isFresh)
        {
            return;
        }

        _entries.Set(track.Id, new PlaceholderEntry(track, now));
    }

    public void Remove(string trackId) => _entries.Remove(trackId);
}
