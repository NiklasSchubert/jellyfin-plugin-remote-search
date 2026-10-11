using System.Globalization;
using System.Text;
using Jellyfin.Plugin.RemoteSearch.Backend;

namespace Jellyfin.Plugin.RemoteSearch.Downloads;

public static class DownloadPaths
{
    private const string Extension = ".ogg";
    private const string UnknownArtist = "Unknown Artist";
    private const string UnknownAlbum = "Unknown Album";
    private const string UnknownTitle = "Unknown Title";
    private const string IgnoredNameEscape = " _";

    // title + " [id]" + ".ogg.part", each escaped, stays under the 255-byte NAME_MAX of Linux file systems
    private const int MaxSegmentBytes = 118;

    // the union of what Linux, Windows and SMB shares reject, so a library on any of them works
    private static readonly char[] ForbiddenCharacters = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

    // Jellyfin's IgnorePatterns.cs: a file or folder with one of these names never becomes a library item
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sample", "minta", "metadata", "ps3_update", "ps3_vprm", "extrafanart", "extrathumbs", "lost+found",
        "subs", "TempRec", "TempSBE", "eaDir", "@eaDir", "#recycle", "@Recycle", "$RECYCLE.BIN",
        "System Volume Information", "thumbs.db",
    };

    private static readonly string[] IgnoredSuffixes = [".sample", ".minta", ".trickplay", ".bts", ".sync"];

    /// <summary><c>root/Artist/Album/Title.ogg</c>.</summary>
    public static string ForTrack(string root, RemoteTrack track)
    {
        var artistFolder = Segment(track.PrimaryArtist, UnknownArtist);
        var albumFolder = Segment(track.Album, UnknownAlbum);
        var fileName = Segment(track.Title, Segment(track.Id, UnknownTitle)) + Extension;
        return Path.Combine(root, artistFolder, albumFolder, fileName);
    }

    /// <summary>Same folder, <c>Title [id].ogg</c>: for a name another track already uses.</summary>
    public static string WithIdSuffix(string path, string trackId)
    {
        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var safeId = Segment(trackId, UnknownTitle);
        return Path.Combine(folder, $"{name} [{safeId}]{Extension}");
    }

    internal static string Segment(string? name, string fallback)
    {
        var replaced = string.Concat((name ?? string.Empty).Select(c => IsForbidden(c) ? '_' : c));
        var shortened = TruncateToUtf8Bytes(TrimEdges(replaced), MaxSegmentBytes);
        var trimmed = TrimEdges(shortened);
        if (trimmed.Length == 0)
        {
            return fallback;
        }

        return IsIgnoredByJellyfin(trimmed) ? trimmed + IgnoredNameEscape : trimmed;
    }

    // Windows drops trailing dots and spaces, Jellyfin skips leading dots as hidden, and "." / ".." are not names
    private static string TrimEdges(string name)
    {
        static bool IsEdge(char character) => char.IsWhiteSpace(character) || character == '.';

        var start = name.TakeWhile(IsEdge).Count();
        var end = name.Length - name.Reverse().TakeWhile(IsEdge).Count();
        return start >= end ? string.Empty : name[start..end];
    }

    // cuts between text elements so no surrogate pair or combining sequence is split
    private static string TruncateToUtf8Bytes(string text, int maxBytes)
    {
        var keptLength = 0;
        var keptBytes = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var elementBytes = Encoding.UTF8.GetByteCount(element);
            if (keptBytes + elementBytes > maxBytes)
            {
                break;
            }

            keptBytes += elementBytes;
            keptLength += element.Length;
        }

        return text[..keptLength];
    }

    private static bool IsIgnoredByJellyfin(string name) =>
        IgnoredNames.Contains(name)
        || IgnoredSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool IsForbidden(char character) =>
        char.IsControl(character) || ForbiddenCharacters.Contains(character);
}
