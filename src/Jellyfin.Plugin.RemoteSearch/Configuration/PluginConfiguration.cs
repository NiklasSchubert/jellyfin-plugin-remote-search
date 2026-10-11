using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RemoteSearch.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string BackendSocketPath { get; set; } = "/run/remote-search/backend.sock";

    /// <summary>Gets or sets the download folder; empty means <c>{DataPath}/remote-search-music</c>.</summary>
    public string DownloadPath { get; set; } = string.Empty;

    public string LibraryName { get; set; } = "Remote Search";

    public int DebounceMs { get; set; } = 300;

    public int SearchTimeoutMs { get; set; } = 6000;

    public int MinTermLength { get; set; } = 3;

    public int MaxResults { get; set; } = 10;

    public int PlaceholderMaxAgeDays { get; set; } = 14;
}
