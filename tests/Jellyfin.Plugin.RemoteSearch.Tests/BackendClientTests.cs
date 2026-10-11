using System.Text.Json;
using Jellyfin.Plugin.RemoteSearch.Backend;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public class BackendClientTests
{
    // docs/BACKEND-API.md, GET /search; unknown fields are ignored
    private const string SearchResponse = """
        [
          {"kind":"track","uri":"demo:track:42","name":"Never Gonna Give You Up",
           "artists":["Rick Astley"],"duration_ms":213573,"album_name":"Whenever You Need Somebody",
           "cover_url":"https://example.com/cover.jpg","explicit":true}
        ]
        """;

    [Fact]
    public void TrackHitBecomesTrackWithTheUriAsId()
    {
        var item = JsonSerializer.Deserialize<List<BackendClient.SearchItem>>(SearchResponse)!.Single();

        var track = BackendClient.ToTrack(item);

        Assert.Equal(
            new RemoteTrack(
                "demo:track:42",
                "Never Gonna Give You Up",
                track.Artists,
                "Whenever You Need Somebody",
                213573,
                "https://example.com/cover.jpg"),
            track);
        Assert.Equal(["Rick Astley"], track.Artists);
    }

    [Theory]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("{\"error\":\"not a list\"}")]
    public void InvalidSearchResponseIsABackendError(string body)
    {
        Assert.Throws<BackendException>(() => BackendClient.ParseSearchResponse(body));
    }
}
