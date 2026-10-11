using Jellyfin.Plugin.RemoteSearch.Search;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public class SearchDebouncerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);
    private static readonly Guid User = Guid.NewGuid();

    private readonly FakeTimeProvider _time = new();
    private readonly SearchDebouncer _debouncer;

    public SearchDebouncerTests()
    {
        _debouncer = new SearchDebouncer(_time);
    }

    [Fact]
    public async Task NewerTermSupersedesTheEarlierKeystroke()
    {
        var earlier = _debouncer.IsStillLatestAfterAsync(User, "daf", Delay, CancellationToken.None);
        _time.Advance(TimeSpan.FromMilliseconds(200));
        var later = _debouncer.IsStillLatestAfterAsync(User, "daft", Delay, CancellationToken.None);
        _time.Advance(Delay);

        Assert.False(await earlier);
        Assert.True(await later);
    }

    [Fact]
    public async Task ParallelRequestsForTheSameTermAllProceed()
    {
        var songs = _debouncer.IsStillLatestAfterAsync(User, "daft punk", Delay, CancellationToken.None);
        var everything = _debouncer.IsStillLatestAfterAsync(User, "daft punk", Delay, CancellationToken.None);
        _time.Advance(Delay);

        Assert.True(await songs);
        Assert.True(await everything);
    }

    [Fact]
    public async Task UsersDoNotDebounceEachOther()
    {
        var first = _debouncer.IsStillLatestAfterAsync(User, "daft", Delay, CancellationToken.None);
        var second = _debouncer.IsStillLatestAfterAsync(Guid.NewGuid(), "queen", Delay, CancellationToken.None);
        _time.Advance(Delay);

        Assert.True(await first);
        Assert.True(await second);
    }

    [Fact]
    public async Task WaitEndsWhenTheRequestIsAborted()
    {
        using var aborted = new CancellationTokenSource();
        var wait = _debouncer.IsStillLatestAfterAsync(User, "daft", Delay, aborted.Token);
        await aborted.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
