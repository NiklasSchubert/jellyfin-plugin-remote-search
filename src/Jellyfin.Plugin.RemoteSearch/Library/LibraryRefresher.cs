using Jellyfin.Plugin.RemoteSearch.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteSearch.Library;

public interface ILibraryRefresher
{
    Task FileAddedAsync(string path);
}

/// <summary>Gets a downloaded file into a music library, putting the download folder into one first if no library has it.</summary>
public sealed class LibraryRefresher : ILibraryRefresher
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly PluginSettings _settings;
    private readonly ILogger<LibraryRefresher> _logger;

    public LibraryRefresher(
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        PluginSettings settings,
        ILogger<LibraryRefresher> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _settings = settings;
        _logger = logger;
    }

    public async Task FileAddedAsync(string path)
    {
        var downloadPath = _settings.DownloadPath;
        var libraries = _libraryManager.GetVirtualFolders();
        var isInLibrary = libraries.Any(library => library.Locations.Any(location => Contains(location, downloadPath)));
        if (!isInLibrary)
        {
            await AddToLibraryAsync(downloadPath, libraries).ConfigureAwait(false);
            return;
        }

        // a scan skips an empty library folder, and a change report only refreshes items it can find above the file
        var isDownloadFolderKnown = _libraryManager.FindByPath(downloadPath, isFolder: true) is not null;
        if (isDownloadFolderKnown)
        {
            _libraryMonitor.ReportFileSystemChanged(path);
        }
        else
        {
            _libraryManager.QueueLibraryScan();
        }
    }

    // only on a download, so the settings can still change after install without leaving a library behind
    private async Task AddToLibraryAsync(string downloadPath, IReadOnlyList<VirtualFolderInfo> libraries)
    {
        try
        {
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
}
