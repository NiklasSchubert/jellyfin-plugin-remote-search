using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Search;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public class SearchCacheTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly SearchCache _cache;
    private int _searchesStarted;

    public SearchCacheTests()
    {
        _cache = new SearchCache(_time);
    }

    [Fact]
    public async Task RunningSearchIsShared()
    {
        var backendSearch = new TaskCompletionSource<IReadOnlyList<RemoteTrack>>();

        var first = _cache.GetOrStart("queen", () => Start(backendSearch.Task));
        var second = _cache.GetOrStart("queen", () => Start(backendSearch.Task));
        backendSearch.SetResult([Tracks.Create()]);

        Assert.Same(await first, await second);
        Assert.Equal(1, _searchesStarted);
    }

    [Fact]
    public void FinishedSearchExpires()
    {
        _cache.GetOrStart("queen", () => Start(Task.FromResult<IReadOnlyList<RemoteTrack>>([])));

        _time.Advance(TimeSpan.FromMinutes(9));
        Assert.NotNull(_cache.Find("queen"));
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(_cache.Find("queen"));
    }

    [Fact]
    public async Task FailedSearchIsNotKept()
    {
        var failing = _cache.GetOrStart("queen", () => Start(Task.FromException<IReadOnlyList<RemoteTrack>>(new BackendException("down"))));
        await Assert.ThrowsAsync<BackendException>(() => failing);

        Assert.Null(_cache.Find("queen"));
    }

    private Task<IReadOnlyList<RemoteTrack>> Start(Task<IReadOnlyList<RemoteTrack>> search)
    {
        _searchesStarted++;
        return search;
    }
}
