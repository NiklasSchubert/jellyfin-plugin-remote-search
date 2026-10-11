using System.Text;
using Jellyfin.Plugin.RemoteSearch.Downloads;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public class DownloadPathsTests
{
    [Fact]
    public void TrackGoesIntoArtistAndAlbumFolders()
    {
        var track = Tracks.Create(title: "Song", artist: "Artist", album: "Album");

        Assert.Equal("/music/Artist/Album/Song.ogg", DownloadPaths.ForTrack("/music", track));
    }

    [Fact]
    public void ForbiddenCharactersAndMissingNamesAreReplaced()
    {
        var track = Tracks.Create(title: "What? / Why: <Now>", artist: "AC/DC", album: null);

        Assert.Equal("/music/AC_DC/Unknown Album/What_ _ Why_ _Now_.ogg", DownloadPaths.ForTrack("/music", track));
    }

    [Theory]
    [InlineData("..", "fallback")]
    [InlineData(" Title. ", "Title")]
    [InlineData("", "fallback")]
    public void SegmentsAreNeverEmptyOrRelative(string name, string expected)
    {
        Assert.Equal(expected, DownloadPaths.Segment(name, "fallback"));
    }

    [Theory]
    [InlineData("...Baby One More Time", "Baby One More Time")]
    [InlineData(". . .hidden", "hidden")]
    public void LeadingDotsAreDroppedSoJellyfinDoesNotHideTheFile(string name, string expected)
    {
        Assert.Equal(expected, DownloadPaths.Segment(name, "fallback"));
    }

    [Theory]
    [InlineData("Sample", "Sample _")]
    [InlineData("METADATA", "METADATA _")]
    [InlineData("Live.Sample", "Live.Sample _")]
    [InlineData("Samples", "Samples")]
    public void NamesJellyfinIgnoresAreEscaped(string name, string expected)
    {
        Assert.Equal(expected, DownloadPaths.Segment(name, "fallback"));
    }

    [Theory]
    [InlineData('音', 37)]
    [InlineData('a', 111)]
    public void LongestNamesFitTheFileNameLimitWithIdSuffixWhileDownloading(char filler, int count)
    {
        var longestEscapedName = new string(filler, count) + ".sample";
        var path = DownloadPaths.ForTrack("/music", Tracks.Create(title: longestEscapedName));

        var partialFile = DownloadPaths.WithIdSuffix(path, longestEscapedName) + ".part";

        Assert.InRange(Encoding.UTF8.GetByteCount(Path.GetFileName(partialFile)), 1, 255);
    }

    [Fact]
    public void TruncationNeverSplitsASurrogatePair()
    {
        var name = new string('a', 117) + "😀";

        Assert.Equal(new string('a', 117), DownloadPaths.Segment(name, "fallback"));
    }

    [Fact]
    public void IdSuffixKeepsFolderAndExtension()
    {
        Assert.Equal("/music/A/B/Song [abc].ogg", DownloadPaths.WithIdSuffix("/music/A/B/Song.ogg", "abc"));
    }

    [Fact]
    public void IdsAreSanitizedLikeNames()
    {
        Assert.Equal("/music/A/B/Song [demo_track_42].ogg", DownloadPaths.WithIdSuffix("/music/A/B/Song.ogg", "demo:track:42"));
    }
}
