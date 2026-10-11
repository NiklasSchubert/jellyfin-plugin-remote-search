using System.Collections.Concurrent;
using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Library;
using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteSearch.Downloads;

/// <summary>Turns a track id into a file in the download folder, downloading at most once.</summary>
public sealed class DownloadService
{
    private const string PartialSuffix = ".part";

    private readonly IBackendClient _backend;
    private readonly Ledger _ledger;
    private readonly PlaceholderStore _placeholders;
    private readonly ILibraryRefresher _libraryRefresher;
    private readonly PluginSettings _settings;
    private readonly ILogger<DownloadService> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _running = new();
    // the backend serializes too; this keeps queued requests out of its own timeout
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public DownloadService(
        IBackendClient backend,
        Ledger ledger,
        PlaceholderStore placeholders,
        ILibraryRefresher libraryRefresher,
        PluginSettings settings,
        ILogger<DownloadService> logger)
    {
        _backend = backend;
        _ledger = ledger;
        _placeholders = placeholders;
        _libraryRefresher = libraryRefresher;
        _settings = settings;
        _logger = logger;
    }

    /// <returns>Path of the downloaded file.</returns>
    /// <exception cref="KeyNotFoundException">The id was never a search hit.</exception>
    /// <exception cref="BackendException">The download failed.</exception>
    public Task<string> GetFileAsync(string trackId, CancellationToken cancellationToken)
    {
        var downloaded = _ledger.Get(trackId);
        if (downloaded is not null && File.Exists(downloaded.Path))
        {
            return Task.FromResult(downloaded.Path);
        }

        // a caller giving up must not cancel the download others may be waiting for
        var download = _running.GetOrAdd(trackId, id => new Lazy<Task<string>>(() => DownloadAndForgetAsync(id)));
        return download.Value.WaitAsync(cancellationToken);
    }

    private async Task<string> DownloadAndForgetAsync(string trackId)
    {
        try
        {
            return await DownloadAsync(trackId).ConfigureAwait(false);
        }
        finally
        {
            _running.TryRemove(trackId, out _);
        }
    }

    private async Task<string> DownloadAsync(string trackId)
    {
        var track = _placeholders.Get(trackId)?.Track
            ?? throw new KeyNotFoundException($"Track {trackId} is not a known search hit");

        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = ChooseFreePath(track);
            if (!File.Exists(path))
            {
                await DownloadRecordedAsync(track, path).ConfigureAwait(false);
            }
            else
            {
                _ledger.Save(NewEntry(track, path));
            }

            _libraryRefresher.FileAdded(path);
            return path;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    // a library scan can pick the file up the moment it is moved in, and must find it in the ledger to promote it
    private async Task DownloadRecordedAsync(RemoteTrack track, string path)
    {
        var previous = _ledger.Get(track.Id);
        _ledger.Save(NewEntry(track, path));
        try
        {
            await DownloadToAsync(track, path).ConfigureAwait(false);
        }
        catch
        {
            if (previous is null)
            {
                _ledger.Remove(track.Id);
            }
            else
            {
                _ledger.Save(previous);
            }

            throw;
        }
    }

    private static LedgerEntry NewEntry(RemoteTrack track, string path) =>
        new(track.Id, track.Title, track.Artists, track.DurationMs, path, LocalItemId: null);

    private string ChooseFreePath(RemoteTrack track)
    {
        var preferred = DownloadPaths.ForTrack(_settings.DownloadPath, track);
        var owner = _ledger.FindByPath(preferred);
        var isTakenByAnotherTrack = File.Exists(preferred) && owner?.TrackId != track.Id;
        return isTakenByAnotherTrack ? DownloadPaths.WithIdSuffix(preferred, track.Id) : preferred;
    }

    private async Task DownloadToAsync(RemoteTrack track, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partialPath = path + PartialSuffix;
        _logger.LogInformation("Downloading {TrackId} ({Title}) to {Path}", track.Id, track.Title, path);
        try
        {
            await _backend.DownloadAsync(track.Id, partialPath, CancellationToken.None).ConfigureAwait(false);
            File.Move(partialPath, path, overwrite: false);
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }
    }
}
