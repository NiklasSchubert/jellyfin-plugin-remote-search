using Jellyfin.Plugin.RemoteSearch.Library;
using Jellyfin.Plugin.RemoteSearch.Storage;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public sealed class CleanupPlaceholdersTaskTests : IDisposable
{
    private static readonly DateTime Cutoff = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TemporaryDirectory _directory = new();
    private readonly Ledger _ledger;

    public CleanupPlaceholdersTaskTests()
    {
        _ledger = new Ledger(_directory.File("ledger.json"));
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void PlaceholderOfALibraryItemGoesEvenIfSeenRecently()
    {
        var placeholder = Seen("promoted", Cutoff.AddDays(1));
        Download("promoted", localItemId: Guid.NewGuid());

        Assert.Equal(["promoted"], CleanupPlaceholdersTask.SelectRemovable([placeholder], _ledger, Cutoff));
    }

    [Fact]
    public void PlaceholderStillTurningIntoALibraryItemStays()
    {
        var placeholder = Seen("downloading", Cutoff.AddDays(-30));
        Download("downloading", localItemId: null);

        Assert.Empty(CleanupPlaceholdersTask.SelectRemovable([placeholder], _ledger, Cutoff));
    }

    [Fact]
    public void OnlyNeverPlayedPlaceholdersNotSeenSinceTheCutoffGo()
    {
        PlaceholderEntry[] placeholders = [Seen("old", Cutoff.AddDays(-1)), Seen("recent", Cutoff.AddDays(1))];

        Assert.Equal(["old"], CleanupPlaceholdersTask.SelectRemovable(placeholders, _ledger, Cutoff));
    }

    private static PlaceholderEntry Seen(string trackId, DateTime lastSeenUtc) => new(Tracks.Create(id: trackId), lastSeenUtc);

    private void Download(string trackId, Guid? localItemId)
    {
        var track = Tracks.Create(id: trackId);
        _ledger.Save(new LedgerEntry(track.Id, track.Title, track.Artists, track.DurationMs, "/music/" + trackId + ".ogg", localItemId));
    }
}
