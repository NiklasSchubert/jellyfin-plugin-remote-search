using Jellyfin.Plugin.RemoteSearch.Storage;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void LedgerSurvivesARestart()
    {
        var entry = new LedgerEntry("id1", "Song", ["Artist"], 1000, "/music/song.ogg", Guid.NewGuid());
        new Ledger(_directory.File("ledger.json")).Save(entry);

        var reloaded = new Ledger(_directory.File("ledger.json"));

        Assert.Equal(entry.LocalItemId, reloaded.Get("id1")?.LocalItemId);
        Assert.Equal("id1", reloaded.FindByPath("/music/song.ogg")?.TrackId);
        Assert.Equal("id1", reloaded.FindByLocalItem(entry.LocalItemId!.Value)?.TrackId);
    }

    [Fact]
    public void OnlyPromotedDownloadsHideSearchHits()
    {
        var ledger = new Ledger(_directory.File("ledger.json"));
        var track = Tracks.Create();
        var entry = new LedgerEntry(track.Id, track.Title, track.Artists, track.DurationMs, "/music/x.ogg", LocalItemId: null);

        ledger.Save(entry);
        Assert.False(ledger.HasInLibrary(track));

        ledger.Save(entry with { LocalItemId = Guid.NewGuid() });
        Assert.True(ledger.HasInLibrary(track));
    }

    [Fact]
    public void SameRecordingUnderAnotherIdIsInLibrary()
    {
        var ledger = new Ledger(_directory.File("ledger.json"));
        var albumVersion = Tracks.Create(id: "album");
        ledger.Save(new LedgerEntry(albumVersion.Id, albumVersion.Title, albumVersion.Artists, albumVersion.DurationMs, "/x.ogg", Guid.NewGuid()));

        var singleVersion = Tracks.Create(id: "single", album: "Single", durationMs: albumVersion.DurationMs + 800);
        var liveVersion = Tracks.Create(id: "live", title: albumVersion.Title + " - Live");

        Assert.True(ledger.HasInLibrary(singleVersion));
        Assert.False(ledger.HasInLibrary(liveVersion));
    }

    [Fact]
    public void PlaceholderStoreSurvivesARestart()
    {
        var path = _directory.File("placeholders.json");
        new PlaceholderStore(path, TimeProvider.System).Touch(Tracks.Create(id: "kept"));

        var reloaded = new PlaceholderStore(path, TimeProvider.System);

        Assert.Equal(Tracks.Create(id: "kept").Artists, reloaded.Get("kept")?.Track.Artists);
    }
}
