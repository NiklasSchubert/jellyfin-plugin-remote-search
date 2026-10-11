using Jellyfin.Plugin.RemoteSearch.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.RemoteSearch.Library;

public interface ILibraryRefresher
{
    void FileAdded(string path);
}

public sealed class LibraryRefresher : ILibraryRefresher
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly PluginSettings _settings;

    public LibraryRefresher(ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, PluginSettings settings)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _settings = settings;
    }

    public void FileAdded(string path)
    {
        // a scan skips an empty library folder, and a change report only refreshes items it can find above the file
        var isDownloadFolderKnown = _libraryManager.FindByPath(_settings.DownloadPath, isFolder: true) is not null;
        if (isDownloadFolderKnown)
        {
            _libraryMonitor.ReportFileSystemChanged(path);
        }
        else
        {
            _libraryManager.QueueLibraryScan();
        }
    }
}
