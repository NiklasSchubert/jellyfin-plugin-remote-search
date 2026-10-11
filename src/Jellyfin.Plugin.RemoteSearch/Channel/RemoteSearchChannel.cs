using Jellyfin.Plugin.RemoteSearch.Downloads;
using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.RemoteSearch.Channel;

/// <summary>Owns the placeholder items; playing one downloads the track.</summary>
public sealed class RemoteSearchChannel : IChannel, IRequiresMediaInfoCallback
{
    public const string ChannelName = "Remote Search";

    private const string OggContainer = "ogg";

    private readonly PlaceholderStore _store;
    private readonly DownloadService _downloads;

    public RemoteSearchChannel(PlaceholderStore store, DownloadService downloads)
    {
        _store = store;
        _downloads = downloads;
    }

    public string Name => ChannelName;

    public string Description => "remote search results. Playing one adds it to your library.";

    public string DataVersion => "1";

    public string HomePageUrl => string.Empty;

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Audio],
        ContentTypes = [ChannelMediaContentType.Song],
    };

    // Kept out of every user's views: the built-in search only matches items under a view, so it can't
    // score placeholders above local songs. Search results and playback reach placeholders by id regardless.
    public bool IsEnabledFor(string userId) => false;

    // browsing deletes every stored channel item not listed here, so list all placeholders
    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var items = _store.All.Select(entry => ToChannelItem(entry.Track)).ToList();
        return Task.FromResult(new ChannelItemResult { Items = items, TotalRecordCount = items.Count });
    }

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken) =>
        Task.FromResult(new DynamicImageResponse { HasImage = false });

    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        var path = await _downloads.GetFileAsync(id, cancellationToken).ConfigureAwait(false);
        var runTimeTicks = _store.Get(id)?.Track.RunTimeTicks;
        return
        [
            new MediaSourceInfo
            {
                Path = path,
                Protocol = MediaProtocol.File,
                Container = OggContainer,
                RunTimeTicks = runTimeTicks,
                // without an audio stream the universal endpoint throws (GetOptimalAudioStream: audioStream null)
                MediaStreams = [VorbisStream()],
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
            },
        ];
    }

    private static MediaStream VorbisStream() => new()
    {
        Type = MediaStreamType.Audio,
        Index = 0,
        Codec = "vorbis",
        Channels = 2,
        SampleRate = 44100,
        BitRate = 160_000,
        IsDefault = true,
    };

    private static ChannelItemInfo ToChannelItem(RemoteTrack track) => new()
    {
        Id = track.Id,
        Name = track.Title,
        Type = ChannelItemType.Media,
        MediaType = ChannelMediaType.Audio,
        ContentType = ChannelMediaContentType.Song,
        Artists = [.. track.Artists],
        AlbumArtists = track.PrimaryArtist is null ? [] : [track.PrimaryArtist],
        RunTimeTicks = track.RunTimeTicks,
        ImageUrl = track.CoverUrl,
    };
}
