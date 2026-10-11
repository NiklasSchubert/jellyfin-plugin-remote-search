using Jellyfin.Data.Enums;
using Jellyfin.Plugin.RemoteSearch.Channel;
using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteSearch.Search;

/// <summary>Merged with Jellyfin's database search; remote hits score below every local match.</summary>
public sealed class RemoteSearchProvider : IInternalSearchProvider
{
    // the built-in provider scores local matches 50..100
    private const float TopPlaceholderScore = 0.9f;
    private const float ScoreStepPerRank = 0.01f;
    private const float LowestPlaceholderScore = 0.01f;

    private readonly IBackendClient _backend;
    private readonly SearchCache _cache;
    private readonly SearchDebouncer _debouncer;
    private readonly Ledger _ledger;
    private readonly PlaceholderItems _placeholders;
    private readonly PluginSettings _settings;
    private readonly ILogger<RemoteSearchProvider> _logger;

    public RemoteSearchProvider(
        IBackendClient backend,
        SearchCache cache,
        SearchDebouncer debouncer,
        Ledger ledger,
        PlaceholderItems placeholders,
        PluginSettings settings,
        ILogger<RemoteSearchProvider> logger)
    {
        _backend = backend;
        _cache = cache;
        _debouncer = debouncer;
        _ledger = ledger;
        _placeholders = placeholders;
        _settings = settings;
        _logger = logger;
    }

    public string Name => Plugin.PluginName;

    public MetadataPluginType Type => MetadataPluginType.SearchProvider;

    public int Priority => 200;

    public bool CanSearch(SearchProviderQuery query)
    {
        var isLongEnough = query.SearchTerm.Trim().Length >= _settings.MinTermLength;
        var wantsAudio = query.IncludeItemTypes.Length == 0 || query.IncludeItemTypes.Contains(BaseItemKind.Audio);
        var excludesAudio = query.ExcludeItemTypes.Contains(BaseItemKind.Audio);
        var wantsAudioMedia = query.MediaTypes.Length == 0 || query.MediaTypes.Contains(MediaType.Audio);
        return isLongEnough && wantsAudio && !excludesAudio && wantsAudioMedia;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchProviderQuery query, CancellationToken cancellationToken)
    {
        var channel = _placeholders.FindChannel();
        if (channel is null)
        {
            _logger.LogDebug("remote search channel not created yet, skipping search");
            return [];
        }

        IReadOnlyList<RemoteTrack> tracks;
        try
        {
            tracks = await FindTracksAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the client moved on (next keystroke, closed page)
            return [];
        }

        var newTracks = tracks.Where(track => !_ledger.HasInLibrary(track)).ToList();
        var results = new List<SearchResult>(newTracks.Count);
        foreach (var track in newTracks)
        {
            var placeholder = _placeholders.Upsert(track, channel);
            results.Add(new SearchResult(placeholder.Id, ScoreForRank(results.Count)));
        }

        return results;
    }

    private async Task<IReadOnlyList<RemoteTrack>> FindTracksAsync(SearchProviderQuery query, CancellationToken cancellationToken)
    {
        var term = query.SearchTerm.Trim();
        var normalizedTerm = TrackMatcher.Normalize(term);
        var search = _cache.Find(normalizedTerm);
        if (search is null)
        {
            var userId = query.UserId ?? Guid.Empty;
            var isLatestQuery = await _debouncer
                .IsStillLatestAfterAsync(userId, normalizedTerm, _settings.Debounce, cancellationToken)
                .ConfigureAwait(false);
            if (!isLatestQuery)
            {
                return [];
            }

            var limit = _settings.MaxResults;
            // not tied to this request: a search that outlives the timeout still fills the cache
            search = _cache.GetOrStart(normalizedTerm, () => SearchBackendAsync(term, limit));
        }

        try
        {
            return await search.WaitAsync(_settings.SearchTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("Remote search for '{Term}' still running after {Timeout}, results come from cache next time", term, _settings.SearchTimeout);
            return [];
        }
        catch (BackendException)
        {
            return [];
        }
    }

    // logs here rather than at the waiting request, which may have timed out long before the search failed
    private async Task<IReadOnlyList<RemoteTrack>> SearchBackendAsync(string term, int limit)
    {
        try
        {
            return await _backend.SearchTracksAsync(term, limit, CancellationToken.None).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            _logger.LogWarning("Remote search for '{Term}' failed: {Error}", term, ex.Message);
            throw;
        }
    }

    private static float ScoreForRank(int rank) =>
        Math.Max(TopPlaceholderScore - (rank * ScoreStepPerRank), LowestPlaceholderScore);
}
