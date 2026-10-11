using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Backend;

namespace Jellyfin.Plugin.RemoteSearch.Storage;

/// <param name="LocalItemId">Set once Jellyfin has the file as a library item ("promoted").</param>
public sealed record LedgerEntry(
    string TrackId,
    string Title,
    string[] Artists,
    long DurationMs,
    string Path,
    Guid? LocalItemId)
{
    public bool IsPromoted => LocalItemId.HasValue;
}

/// <summary>Tracks the plugin downloaded, keyed by track id.</summary>
public sealed class Ledger
{
    private const string FileName = "ledger.json";

    private readonly JsonMap<LedgerEntry> _entries;

    public Ledger(PluginSettings settings)
        : this(Path.Combine(settings.StatePath, FileName))
    {
    }

    internal Ledger(string filePath)
    {
        _entries = new JsonMap<LedgerEntry>(filePath);
    }

    public LedgerEntry? Get(string trackId) => _entries.Get(trackId);

    public LedgerEntry? FindByPath(string path) =>
        _entries.Values.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));

    public LedgerEntry? FindByLocalItem(Guid itemId) =>
        _entries.Values.FirstOrDefault(entry => entry.LocalItemId == itemId);

    public void Save(LedgerEntry entry) => _entries.Set(entry.TrackId, entry);

    public void Remove(string trackId) => _entries.Remove(trackId);

    /// <summary>Whether this track, or the same recording from another release, is already a library item.</summary>
    public bool HasInLibrary(RemoteTrack track) =>
        _entries.Values
            .Where(entry => entry.IsPromoted)
            .Any(entry => entry.TrackId == track.Id || IsSameRecording(entry, track));

    private static bool IsSameRecording(LedgerEntry entry, RemoteTrack track) =>
        TrackMatcher.IsSameRecording(
            new TrackSignature(entry.Title, entry.Artists.FirstOrDefault(), entry.DurationMs),
            new TrackSignature(track.Title, track.PrimaryArtist, track.DurationMs));
}
