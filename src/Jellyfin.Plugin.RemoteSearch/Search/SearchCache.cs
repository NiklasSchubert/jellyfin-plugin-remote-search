using System.Collections.Concurrent;
using Jellyfin.Plugin.RemoteSearch.Backend;

namespace Jellyfin.Plugin.RemoteSearch.Search;

/// <summary>Backend searches by normalized term, shared while running and kept for a while after.</summary>
public sealed class SearchCache
{
    private const int MaxEntries = 500;
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, CachedSearch> _searches = new();
    private readonly TimeProvider _timeProvider;

    public SearchCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <returns>A running or finished search for the term, or null if there is none.</returns>
    public Task<IReadOnlyList<RemoteTrack>>? Find(string normalizedTerm)
    {
        var hasEntry = _searches.TryGetValue(normalizedTerm, out var cached);
        var isUnusable = hasEntry && (cached!.ExpiresAt <= _timeProvider.GetUtcNow() || cached.HasFailed);
        if (isUnusable)
        {
            _searches.TryRemove(KeyValuePair.Create(normalizedTerm, cached!));
            return null;
        }

        return cached?.Search;
    }

    public Task<IReadOnlyList<RemoteTrack>> GetOrStart(string normalizedTerm, Func<Task<IReadOnlyList<RemoteTrack>>> startSearch)
    {
        var existing = Find(normalizedTerm);
        if (existing is not null)
        {
            return existing;
        }

        EvictExpired();
        var candidate = new CachedSearch(new Lazy<Task<IReadOnlyList<RemoteTrack>>>(startSearch), _timeProvider.GetUtcNow() + EntryLifetime);
        return _searches.GetOrAdd(normalizedTerm, candidate).Search;
    }

    private void EvictExpired()
    {
        if (_searches.Count < MaxEntries)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        foreach (var (term, entry) in _searches)
        {
            if (entry.ExpiresAt <= now)
            {
                _searches.TryRemove(KeyValuePair.Create(term, entry));
            }
        }

        if (_searches.Count >= MaxEntries)
        {
            _searches.Clear();
        }
    }

    private sealed record CachedSearch(Lazy<Task<IReadOnlyList<RemoteTrack>>> Start, DateTimeOffset ExpiresAt)
    {
        public Task<IReadOnlyList<RemoteTrack>> Search => Start.Value;

        public bool HasFailed => Start.IsValueCreated && (Search.IsFaulted || Search.IsCanceled);
    }
}
