using Jellyfin.Plugin.RemoteSearch.Channel;
using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteSearch.Library;

public sealed class CleanupPlaceholdersTask : IScheduledTask
{
    private static readonly TimeSpan RunAt = TimeSpan.FromHours(3);

    private readonly PlaceholderStore _store;
    private readonly PlaceholderItems _placeholders;
    private readonly Ledger _ledger;
    private readonly PluginSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CleanupPlaceholdersTask> _logger;

    public CleanupPlaceholdersTask(
        PlaceholderStore store,
        PlaceholderItems placeholders,
        Ledger ledger,
        PluginSettings settings,
        TimeProvider timeProvider,
        ILogger<CleanupPlaceholdersTask> logger)
    {
        _store = store;
        _placeholders = placeholders;
        _ledger = ledger;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Name => "Remove old remote results";

    public string Key => "RemoteSearchCleanupPlaceholders";

    public string Description => "Removes remote search results that were never played and not seen in a search for a while.";

    public string Category => Plugin.PluginName;

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _settings.PlaceholderMaxAge;
        var removable = SelectRemovable(_store.All, _ledger, cutoff);
        for (var index = 0; index < removable.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _placeholders.Delete(removable[index]);
            progress.Report(100.0 * (index + 1) / removable.Count);
        }

        _logger.LogInformation("Removed {Count} remote results", removable.Count);
        return Task.CompletedTask;
    }

    /// <returns>Track ids of placeholders whose track is now a library item, or that were never played and not seen since <paramref name="cutoffUtc"/>.</returns>
    internal static IReadOnlyList<string> SelectRemovable(IEnumerable<PlaceholderEntry> placeholders, Ledger ledger, DateTime cutoffUtc)
    {
        return placeholders
            .Where(entry =>
            {
                var download = ledger.Get(entry.Track.Id);
                var isInLibrary = download?.IsPromoted ?? false;
                var isStaleAndNeverPlayed = download is null && entry.LastSeenUtc < cutoffUtc;
                return isInLibrary || isStaleAndNeverPlayed;
            })
            .Select(entry => entry.Track.Id)
            .ToList();
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = RunAt.Ticks },
    ];
}
