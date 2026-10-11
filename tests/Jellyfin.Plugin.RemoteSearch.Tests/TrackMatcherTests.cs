using Jellyfin.Plugin.RemoteSearch.Storage;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public class TrackMatcherTests
{
    private static readonly TrackSignature Original = new("Bohemian Rhapsody", "Queen", 354_000);

    [Theory]
    [InlineData("Beyoncé – Halo!", "beyonce halo")]
    [InlineData("  AC/DC  ", "ac dc")]
    [InlineData("Sigur Rós", "sigur ros")]
    [InlineData(null, "")]
    public void NormalizeDropsCaseDiacriticsAndPunctuation(string? input, string expected)
    {
        Assert.Equal(expected, TrackMatcher.Normalize(input));
    }

    [Fact]
    public void SameRecordingFromAnotherReleaseMatches()
    {
        var compilationCopy = new TrackSignature("bohemian rhapsody", "QUEEN", 355_500);

        Assert.True(TrackMatcher.IsSameRecording(Original, compilationCopy));
    }

    [Theory]
    [InlineData("Bohemian Rhapsody - Remastered 2011", "Queen", 354_000)]
    [InlineData("Bohemian Rhapsody", "Panic! At The Disco", 354_000)]
    [InlineData("Bohemian Rhapsody", "Queen", 420_000)]
    public void VersionsAndCoversStayApart(string title, string artist, long durationMs)
    {
        var other = new TrackSignature(title, artist, durationMs);

        Assert.False(TrackMatcher.IsSameRecording(Original, other));
    }
}
