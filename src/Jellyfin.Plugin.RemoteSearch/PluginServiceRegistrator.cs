using Jellyfin.Plugin.RemoteSearch.Channel;
using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Downloads;
using Jellyfin.Plugin.RemoteSearch.Library;
using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Search;
using Jellyfin.Plugin.RemoteSearch.Storage;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.RemoteSearch;

/// <summary>The search provider and the scheduled task are found by Jellyfin itself; this wires their dependencies.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<PluginSettings>();
        serviceCollection.AddSingleton<IBackendClient, BackendClient>();
        serviceCollection.AddSingleton<Ledger>();
        serviceCollection.AddSingleton<PlaceholderStore>();
        serviceCollection.AddSingleton<PlaceholderItems>();
        serviceCollection.AddSingleton<SearchCache>();
        serviceCollection.AddSingleton<SearchDebouncer>();
        serviceCollection.AddSingleton<ILibraryRefresher, LibraryRefresher>();
        serviceCollection.AddSingleton<DownloadService>();
        serviceCollection.AddSingleton<IChannel, RemoteSearchChannel>();
        serviceCollection.AddHostedService<LibrarySync>();
    }
}
