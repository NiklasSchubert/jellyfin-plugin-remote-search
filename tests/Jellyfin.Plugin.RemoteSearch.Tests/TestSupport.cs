using System.Reflection;
using Jellyfin.Plugin.RemoteSearch.Backend;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

internal static class Tracks
{
    public static RemoteTrack Create(
        string id = "demo:track:42",
        string title = "Never Gonna Give You Up",
        string artist = "Rick Astley",
        string? album = "Whenever You Need Somebody",
        long durationMs = 213_573) =>
        new(id, title, [artist], album, durationMs, CoverUrl: null);
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = Directory.CreateTempSubdirectory("remote-search-tests").FullName;
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

/// <summary>Every path of <see cref="IApplicationPaths"/> points into one directory.</summary>
public class ApplicationPathsStub : DispatchProxy
{
    private string _root = string.Empty;

    public static IApplicationPaths Create(string root)
    {
        var proxy = Create<IApplicationPaths, ApplicationPathsStub>();
        ((ApplicationPathsStub)(object)proxy)._root = root;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.ReturnType == typeof(string) ? _root : null;
}
