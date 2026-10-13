using System.Reflection;
using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Library;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public sealed class LibraryRefresherTests : IDisposable
{
    private const string DefaultLibraryName = "Remote Search";

    private readonly TemporaryDirectory _directory = new();
    private readonly PluginSettings _settings;
    private readonly RecordingLibrary _library;
    private readonly RecordingMonitor _monitor;
    private readonly LibraryRefresher _refresher;

    public LibraryRefresherTests()
    {
        _settings = new PluginSettings(ApplicationPathsStub.Create(_directory.Path));
        _library = RecordingLibrary.Create();
        _monitor = RecordingMonitor.Create();
        _refresher = new LibraryRefresher((ILibraryManager)(object)_library, (ILibraryMonitor)(object)_monitor, _settings, NullLogger<LibraryRefresher>.Instance);
    }

    private string DownloadedFile => Path.Combine(_settings.DownloadPath, "Artist", "Album", "Song.ogg");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task StartupLeavesTheLibrariesAlone()
    {
        var sync = new LibrarySync((ILibraryManager)(object)_library, new Ledger(_settings), NullLogger<LibrarySync>.Instance);

        await sync.StartAsync(CancellationToken.None);

        Assert.All(_library.Calls, call => Assert.StartsWith("add_", call, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FirstDownloadCreatesTheLibrary()
    {
        await _refresher.FileAddedAsync(DownloadedFile);

        Assert.Equal([$"AddVirtualFolder {DefaultLibraryName} {_settings.DownloadPath}"], _library.Changes);
    }

    [Fact]
    public async Task FirstDownloadJoinsALibraryOfTheConfiguredName()
    {
        _library.Folders.Add(new VirtualFolderInfo { Name = DefaultLibraryName, Locations = ["/music"] });

        await _refresher.FileAddedAsync(DownloadedFile);

        Assert.Equal([$"AddMediaPath {DefaultLibraryName} {_settings.DownloadPath}", "QueueLibraryScan"], _library.Changes);
    }

    [Fact]
    public async Task DownloadIntoAKnownFolderIsReportedAsAChange()
    {
        _library.Folders.Add(new VirtualFolderInfo { Name = "Music", Locations = [_directory.Path] });
        _library.KnowsDownloadFolder = true;

        await _refresher.FileAddedAsync(DownloadedFile);

        Assert.Empty(_library.Changes);
        Assert.Equal([DownloadedFile], _monitor.ReportedPaths);
    }

    [Fact]
    public async Task DownloadIntoAFolderTheScanSkippedQueuesAScan()
    {
        _library.Folders.Add(new VirtualFolderInfo { Name = "Music", Locations = [_settings.DownloadPath] });

        await _refresher.FileAddedAsync(DownloadedFile);

        Assert.Equal(["QueueLibraryScan"], _library.Changes);
        Assert.Empty(_monitor.ReportedPaths);
    }

    public class RecordingLibrary : DispatchProxy
    {
        public List<VirtualFolderInfo> Folders { get; } = [];

        public bool KnowsDownloadFolder { get; set; }

        public List<string> Calls { get; } = [];

        public List<string> Changes { get; } = [];

        public static RecordingLibrary Create() => (RecordingLibrary)(object)Create<ILibraryManager, RecordingLibrary>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? string.Empty;
            Calls.Add(name);
            switch (name)
            {
                case nameof(ILibraryManager.GetVirtualFolders):
                    return Folders;
                case nameof(ILibraryManager.FindByPath):
                    return KnowsDownloadFolder ? new Folder() : null;
                case nameof(ILibraryManager.AddVirtualFolder):
                    var options = (LibraryOptions)args![2]!;
                    Changes.Add($"{name} {args[0]} {options.PathInfos.Single().Path}");
                    return Task.CompletedTask;
                case nameof(ILibraryManager.AddMediaPath):
                    Changes.Add($"{name} {args![0]} {((MediaPathInfo)args[1]!).Path}");
                    return null;
                case nameof(ILibraryManager.QueueLibraryScan):
                    Changes.Add(name);
                    return null;
                default:
                    return null;
            }
        }
    }

    public class RecordingMonitor : DispatchProxy
    {
        public List<string> ReportedPaths { get; } = [];

        public static RecordingMonitor Create() => (RecordingMonitor)(object)Create<ILibraryMonitor, RecordingMonitor>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryMonitor.ReportFileSystemChanged))
            {
                ReportedPaths.Add((string)args![0]!);
            }

            return null;
        }
    }
}
