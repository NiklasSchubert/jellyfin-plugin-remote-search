using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteSearch.Library;

/// <summary>
/// Keeps the download folder in a music library and records which library item each download became.
/// </summary>
public sealed class LibrarySync : IHostedService
{
    public const string ProviderIdKey = "RemoteSearch";

    private readonly ILibraryManager _libraryManager;
    private readonly Ledger _ledger;
    private readonly PluginSettings _settings;
    private readonly ILogger<LibrarySync> _logger;

    public LibrarySync(
        ILibraryManager libraryManager,
        Ledger ledger,
        PluginSettings settings,
        ILogger<LibrarySync> logger)
    {
        _libraryManager = libraryManager;
        _ledger = ledger;
        _settings = settings;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
        }

        await EnsureDownloadPathInLibraryAsync().ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    private async void OnConfigurationChanged(object? sender, BasePluginConfiguration configuration)
    {
        await EnsureDownloadPathInLibraryAsync().ConfigureAwait(false);
    }

    private async Task EnsureDownloadPathInLibraryAsync()
    {
        var downloadPath = _settings.DownloadPath;
        try
        {
            Directory.CreateDirectory(downloadPath);
            var libraries = _libraryManager.GetVirtualFolders();
            var isCovered = libraries.Any(library => library.Locations.Any(location => Contains(location, downloadPath)));
            if (isCovered)
            {
                return;
            }

            var libraryName = _settings.LibraryName;
            var namedLibrary = libraries.FirstOrDefault(library => string.Equals(library.Name, libraryName, StringComparison.OrdinalIgnoreCase));
            if (namedLibrary is not null)
            {
                _logger.LogInformation("Adding {Path} to library {Library}", downloadPath, namedLibrary.Name);
                _libraryManager.AddMediaPath(namedLibrary.Name, new MediaPathInfo(downloadPath));
                _libraryManager.QueueLibraryScan();
                return;
            }

            _logger.LogInformation("Creating music library {Library} for {Path}", libraryName, downloadPath);
            var options = new LibraryOptions { PathInfos = [new MediaPathInfo(downloadPath)] };
            await _libraryManager.AddVirtualFolder(libraryName, CollectionTypeOptions.music, options, refreshLibrary: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not put {Path} into a library; downloads won't show up as library items", downloadPath);
        }
    }

    private static bool Contains(string folder, string path)
    {
        var normalizedFolder = Path.TrimEndingDirectorySeparator(folder);
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        return normalizedPath == normalizedFolder
            || normalizedPath.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not Audio { Path: { } path } item)
        {
            return;
        }

        var entry = _ledger.FindByPath(path);
        if (entry is null || entry.LocalItemId == item.Id)
        {
            return;
        }

        // ItemAdded fires inside the library scan; don't hold it up
        _ = Task.Run(() => PromoteAsync(item, entry));
    }

    private async Task PromoteAsync(Audio item, LedgerEntry entry)
    {
        var isWritten = await TryWriteProviderIdAsync(item, entry).ConfigureAwait(false);
        if (isWritten)
        {
            _ledger.Save(entry with { LocalItemId = item.Id });
            _logger.LogInformation("{Title} is now library item {ItemId}; its remote result goes in the next cleanup", entry.Title, item.Id);
        }
    }

    // a metadata refresh racing the promotion saves its own copy of the item, without the provider id
    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not Audio item)
        {
            return;
        }

        var entry = _ledger.FindByLocalItem(item.Id);
        var hasLostProviderId = entry is not null && item.GetProviderId(ProviderIdKey) != entry.TrackId;
        if (!hasLostProviderId)
        {
            return;
        }

        _ = Task.Run(() => TryWriteProviderIdAsync(item, entry!));
    }

    private async Task<bool> TryWriteProviderIdAsync(Audio item, LedgerEntry entry)
    {
        try
        {
            item.SetProviderId(ProviderIdKey, entry.TrackId);
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the track id of {Path} to its library item", entry.Path);
            return false;
        }
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        var entry = _ledger.FindByLocalItem(e.Item.Id);
        if (entry is not null)
        {
            _logger.LogInformation("{Title} left the library, remote results may show it again", entry.Title);
            _ledger.Remove(entry.TrackId);
        }
    }
}
