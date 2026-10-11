namespace Jellyfin.Plugin.RemoteSearch.Backend;

/// <param name="Id">The backend's opaque <c>uri</c>, sent back unchanged to download the track.</param>
public sealed record RemoteTrack(
    string Id,
    string Title,
    string[] Artists,
    string? Album,
    long DurationMs,
    string? CoverUrl)
{
    public string? PrimaryArtist => Artists.FirstOrDefault();

    public long RunTimeTicks => TimeSpan.FromMilliseconds(DurationMs).Ticks;
}
