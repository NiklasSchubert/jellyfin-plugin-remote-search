using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.RemoteSearch.Configuration;

/// <summary>Current plugin configuration with defaults resolved; reads the live config on every access.</summary>
public sealed class PluginSettings
{
    private const string DefaultDownloadFolderName = "remote-search-music";
    private const string StateFolderName = "remote-search";
    private const int MinSearchTimeoutMs = 100;

    private readonly IApplicationPaths _applicationPaths;

    public PluginSettings(IApplicationPaths applicationPaths)
    {
        _applicationPaths = applicationPaths;
    }

    private static PluginConfiguration Current => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public string BackendSocketPath => Current.BackendSocketPath;

    public string DownloadPath
    {
        get
        {
            var configured = Current.DownloadPath.Trim();
            return configured.Length > 0
                ? configured
                : Path.Combine(_applicationPaths.DataPath, DefaultDownloadFolderName);
        }
    }

    public string StatePath => Path.Combine(_applicationPaths.DataPath, StateFolderName);

    public string LibraryName => Current.LibraryName;

    public TimeSpan Debounce => TimeSpan.FromMilliseconds(Math.Max(0, Current.DebounceMs));

    public TimeSpan SearchTimeout => TimeSpan.FromMilliseconds(Math.Max(MinSearchTimeoutMs, Current.SearchTimeoutMs));

    public int MinTermLength => Math.Max(1, Current.MinTermLength);

    public int MaxResults => Math.Max(1, Current.MaxResults);

    public TimeSpan PlaceholderMaxAge => TimeSpan.FromDays(Math.Max(1, Current.PlaceholderMaxAgeDays));
}
