using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RemoteSearch.Storage;

public sealed record TrackSignature(string Title, string? PrimaryArtist, long DurationMs);

/// <summary>
/// Spots the same recording released twice (single and album, compilations) without an ISRC.
/// Versions keep apart: remasters, live takes and edits differ in title suffix or length, covers in artist.
/// </summary>
public static partial class TrackMatcher
{
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2);

    public static bool IsSameRecording(TrackSignature first, TrackSignature second)
    {
        var durationDifference = TimeSpan.FromMilliseconds(Math.Abs(first.DurationMs - second.DurationMs));
        var hasSameLength = durationDifference <= DurationTolerance;
        var hasSameTitle = Normalize(first.Title) == Normalize(second.Title);
        var hasSameArtist = Normalize(first.PrimaryArtist) == Normalize(second.PrimaryArtist);
        return hasSameLength && hasSameTitle && hasSameArtist;
    }

    /// <summary>Lowercase, without diacritics, every run of non-alphanumerics collapsed to one space.</summary>
    public static string Normalize(string? text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var withoutDiacritics = string.Concat(decomposed.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
        return NonAlphanumericRun().Replace(withoutDiacritics, " ").Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonAlphanumericRun();
}
