using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteSearch.Library;

/// <summary>Records which library item each download became.</summary>
public sealed class LibrarySync : IHostedService
{
    public const string ProviderIdKey = "RemoteSearch";

    private readonly ILibraryManager _libraryManager;
    private readonly Ledger _ledger;
    private readonly ILogger<LibrarySync> _logger;

    public LibrarySync(
        ILibraryManager libraryManager,
        Ledger ledger,
        ILogger<LibrarySync> logger)
    {
        _libraryManager = libraryManager;
        _ledger = ledger;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not Audio { Path: { } path } item)
        {
            return;
        }

        var entry = _ledger.FindByPath(path);
        if (entry is null || entry.LocalItemId == item.Id)
        {
            return;
        }

        // ItemAdded fires inside the library scan; don't hold it up
        _ = Task.Run(() => PromoteAsync(item, entry));
    }

    private async Task PromoteAsync(Audio item, LedgerEntry entry)
    {
        var isWritten = await TryWriteProviderIdAsync(item, entry).ConfigureAwait(false);
        if (isWritten)
        {
            _ledger.Save(entry with { LocalItemId = item.Id });
            _logger.LogInformation("{Title} is now library item {ItemId}; its remote result goes in the next cleanup", entry.Title, item.Id);
        }
    }

    // a metadata refresh racing the promotion saves its own copy of the item, without the provider id
    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not Audio item)
        {
            return;
        }

        var entry = _ledger.FindByLocalItem(item.Id);
        var hasLostProviderId = entry is not null && item.GetProviderId(ProviderIdKey) != entry.TrackId;
        if (!hasLostProviderId)
        {
            return;
        }

        _ = Task.Run(() => TryWriteProviderIdAsync(item, entry!));
    }

    private async Task<bool> TryWriteProviderIdAsync(Audio item, LedgerEntry entry)
    {
        try
        {
            item.SetProviderId(ProviderIdKey, entry.TrackId);
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the track id of {Path} to its library item", entry.Path);
            return false;
        }
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        var entry = _ledger.FindByLocalItem(e.Item.Id);
        if (entry is not null)
        {
            _logger.LogInformation("{Title} left the library, remote results may show it again", entry.Title);
            _ledger.Remove(entry.TrackId);
        }
    }
}
