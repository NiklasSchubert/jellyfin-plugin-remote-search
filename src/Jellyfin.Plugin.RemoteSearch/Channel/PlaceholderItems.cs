using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using JellyfinChannel = MediaBrowser.Controller.Channels.Channel;

namespace Jellyfin.Plugin.RemoteSearch.Channel;

/// <summary>remote hits as channel <see cref="Audio"/> items, so search providers can return their ids.</summary>
public sealed class PlaceholderItems
{
    // ChannelManager.GetIdToHash appends this; channel browsing and search must agree on item ids
    private const string ChannelItemIdVersion = "16";

    private readonly ILibraryManager _libraryManager;
    private readonly PlaceholderStore _store;

    public PlaceholderItems(ILibraryManager libraryManager, PlaceholderStore store)
    {
        _libraryManager = libraryManager;
        _store = store;
    }

    /// <returns>Null until Jellyfin has created the channel entity (shortly after startup).</returns>
    public JellyfinChannel? FindChannel()
    {
        var channelId = _libraryManager.GetNewItemId("Channel " + RemoteSearchChannel.ChannelName, typeof(JellyfinChannel));
        return _libraryManager.GetItemById(channelId) as JellyfinChannel;
    }

    public Guid ItemIdFor(string trackId) =>
        _libraryManager.GetNewItemId(trackId + RemoteSearchChannel.ChannelName + ChannelItemIdVersion, typeof(Audio));

    public Audio Upsert(RemoteTrack track, JellyfinChannel channel)
    {
        _store.Touch(track);
        var itemId = ItemIdFor(track.Id);
        if (_libraryManager.GetItemById(itemId) is Audio existing)
        {
            return existing;
        }

        var placeholder = new Audio
        {
            Id = itemId,
            Name = track.Title,
            Artists = track.Artists,
            AlbumArtists = track.PrimaryArtist is null ? [] : [track.PrimaryArtist],
            Album = track.Album,
            RunTimeTicks = track.RunTimeTicks,
            ChannelId = channel.Id,
            ParentId = channel.Id,
            ExternalId = track.Id,
            DateCreated = DateTime.UtcNow,
        };
        if (track.CoverUrl is not null)
        {
            placeholder.SetImagePath(ImageType.Primary, track.CoverUrl);
        }

        // queries group by this key; left empty, all placeholders collapse into one hit
        placeholder.PresentationUniqueKey = placeholder.CreatePresentationUniqueKey();
        _libraryManager.CreateItem(placeholder, channel);
        return placeholder;
    }

    public void Delete(string trackId)
    {
        _store.Remove(trackId);
        var item = _libraryManager.GetItemById(ItemIdFor(trackId));
        if (item is not null)
        {
            _libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false });
        }
    }
}
