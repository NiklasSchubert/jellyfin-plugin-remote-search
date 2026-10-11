using System.Collections.Concurrent;

namespace Jellyfin.Plugin.RemoteSearch.Search;

/// <summary>
/// Per-user debounce across independent search requests. Requests for the same term count as one,
/// because a client fires several at once (one per item type).
/// </summary>
public sealed class SearchDebouncer
{
    private readonly ConcurrentDictionary<Guid, string> _latestTermByUser = new();
    private readonly TimeProvider _timeProvider;

    public SearchDebouncer(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <returns>False when the user searched for a different term during <paramref name="delay"/>.</returns>
    public async Task<bool> IsStillLatestAfterAsync(Guid userId, string normalizedTerm, TimeSpan delay, CancellationToken cancellationToken)
    {
        _latestTermByUser[userId] = normalizedTerm;
        await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        return _latestTermByUser.TryGetValue(userId, out var latestTerm) && latestTerm == normalizedTerm;
    }
}
