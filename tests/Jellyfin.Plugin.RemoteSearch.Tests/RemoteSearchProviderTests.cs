using System.Reflection;
using Jellyfin.Plugin.RemoteSearch.Channel;
using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Search;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using JellyfinChannel = MediaBrowser.Controller.Channels.Channel;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public sealed class RemoteSearchProviderTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly RemoteSearchProvider _provider;

    public RemoteSearchProviderTests()
    {
        var settings = new PluginSettings(ApplicationPathsStub.Create(_directory.Path));
        var placeholders = new PlaceholderItems(LibraryWithChannel.Create(), new PlaceholderStore(settings, TimeProvider.System));
        _provider = new RemoteSearchProvider(
            new UnusedBackend(),
            new SearchCache(TimeProvider.System),
            new SearchDebouncer(TimeProvider.System),
            new Ledger(settings),
            placeholders,
            settings,
            NullLogger<RemoteSearchProvider>.Instance);
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task RequestAbortedDuringTheDebounceFindsNothingWithoutThrowing()
    {
        using var aborted = new CancellationTokenSource();
        var query = new SearchProviderQuery { SearchTerm = "teenage dirtbag", UserId = Guid.NewGuid() };

        var search = _provider.SearchAsync(query, aborted.Token);
        await aborted.CancelAsync();

        Assert.Empty(await search);
    }

    private sealed class UnusedBackend : IBackendClient
    {
        public Task<IReadOnlyList<RemoteTrack>> SearchTracksAsync(string term, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("an aborted request must not reach the backend");

        public Task DownloadAsync(string trackUri, string destinationPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Knows exactly one item, the remote search channel.</summary>
    public class LibraryWithChannel : DispatchProxy
    {
        private readonly JellyfinChannel _channel = new();

        public static ILibraryManager Create() => Create<ILibraryManager, LibraryWithChannel>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(ILibraryManager.GetNewItemId) => Guid.Empty,
            nameof(ILibraryManager.GetItemById) => _channel,
            _ => throw new NotSupportedException(targetMethod?.Name),
        };
    }
}
