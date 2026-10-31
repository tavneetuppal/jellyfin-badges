using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// Pure helpers that turn item data into badge text: edition names, audio labels and ratings.
/// </summary>
public static partial class BadgeInfo
{
    // Known edition keywords (matched in the file or folder name) -> label shown on the poster.
    private static readonly (Regex Pattern, string Label)[] Editions =
    [
        (new Regex(@"director'?s[ ._-]?cut", RegexOptions.IgnoreCase), "DIRECTOR'S CUT"),
        (new Regex(@"\bfinal[ ._-]?cut\b", RegexOptions.IgnoreCase), "FINAL CUT"),
        (new Regex(@"\bextended\b", RegexOptions.IgnoreCase), "EXTENDED EDITION"),
        (new Regex(@"\bunrated\b", RegexOptions.IgnoreCase), "UNRATED"),
        (new Regex(@"\bultimate[ ._-]?(edition|cut)\b", RegexOptions.IgnoreCase), "ULTIMATE EDITION"),
        (new Regex(@"\bspecial[ ._-]?edition\b", RegexOptions.IgnoreCase), "SPECIAL EDITION"),
        (new Regex(@"\banniversary[ ._-]?edition\b", RegexOptions.IgnoreCase), "ANNIVERSARY EDITION"),
        (new Regex(@"\bcriterion\b", RegexOptions.IgnoreCase), "CRITERION"),
        (new Regex(@"\bremastered\b", RegexOptions.IgnoreCase), "REMASTERED"),
        (new Regex(@"\btheatrical\b", RegexOptions.IgnoreCase), "THEATRICAL CUT"),
    ];

    /// <summary>
    /// Finds the edition from a media path: Jellyfin/Plex style <c>{edition-Name}</c> first, then common keywords.
    /// </summary>
    /// <param name="path">Media file path.</param>
    /// <returns>Upper-case edition label, or null.</returns>
    public static string? Edition(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        // File name and its folder; earlier folders (library roots) are ignored.
        var file = Path.GetFileNameWithoutExtension(path);
        var folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
        foreach (var name in new[] { file, folder })
        {
            var tag = EditionTag().Match(name);
            if (tag.Success)
            {
                return tag.Groups[1].Value.Trim().ToUpperInvariant();
            }
        }

        foreach (var name in new[] { file, folder })
        {
            // Only look after the year so titles like "The Final Cut (2004)" aren't read as an edition.
            var year = Year().Match(name);
            if (!year.Success)
            {
                continue;
            }

            var rest = name[(year.Index + year.Length)..];
            foreach (var (pattern, label) in Editions)
            {
                if (pattern.IsMatch(rest))
                {
                    return label;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the release source from the file or folder name (e.g. Radarr's "Bluray-1080p", "WEBDL-1080p", "Remux-2160p").
    /// </summary>
    /// <param name="path">Media file path.</param>
    /// <returns>"bluray", "web", "hdtv", "dvd" or "unknown".</returns>
    public static string Source(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "unknown";
        }

        var names = Path.GetFileNameWithoutExtension(path) + " " + (Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty);
        return BluRaySource().IsMatch(names) ? "bluray"
            : WebSource().IsMatch(names) ? "web"
            : HdtvSource().IsMatch(names) ? "hdtv"
            : DvdSource().IsMatch(names) ? "dvd"
            : "unknown";
    }

    /// <summary>
    /// Builds the audio label for the best audio track, e.g. ("DOLBY", "TrueHD 7.1").
    /// </summary>
    /// <param name="streams">Item media streams.</param>
    /// <returns>Brand and detail, or null when there is no audio.</returns>
    public static (string Brand, string Detail)? Audio(IEnumerable<MediaStream> streams)
    {
        var best = streams
            .Where(s => s.Type == MediaStreamType.Audio)
            .OrderByDescending(s => CodecRank(s))
            .ThenByDescending(s => s.Channels ?? 0)
            .FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var codec = (best.Codec ?? string.Empty).ToLowerInvariant();
        var profile = best.Profile ?? string.Empty;
        var (brand, name) = codec switch
        {
            "truehd" => ("DOLBY", "TrueHD"),
            "eac3" => ("DOLBY", "Digital+"),
            "ac3" => ("DOLBY", "Digital"),
            "dts" when profile.Contains("MA", StringComparison.Ordinal) => ("DTS", "HD MA"),
            "dts" when profile.Contains("HRA", StringComparison.Ordinal) || profile.Contains("HD", StringComparison.Ordinal) => ("DTS", "HD"),
            "dts" => ("DTS", string.Empty),
            "flac" => ("FLAC", string.Empty),
            "aac" => ("AAC", string.Empty),
            "opus" => ("OPUS", string.Empty),
            "mp3" => ("MP3", string.Empty),
            _ when codec.StartsWith("pcm", StringComparison.Ordinal) => ("PCM", string.Empty),
            _ => (codec.ToUpperInvariant(), string.Empty),
        };

        var channels = best.Channels switch
        {
            null or 0 => string.Empty,
            1 => "Mono",
            2 => "Stereo",
            8 => "7.1",
            7 => "6.1",
            6 => "5.1",
            int n => n.ToString(CultureInfo.InvariantCulture) + "ch",
        };

        var detail = string.Join(' ', new[] { name, channels }.Where(s => s.Length > 0));
        return (brand, detail);
    }

    /// <summary>
    /// Formats a community rating (0-10) for display, e.g. "7.8".
    /// </summary>
    /// <param name="rating">Rating.</param>
    /// <returns>Text, or null when missing.</returns>
    public static string? Imdb(float? rating)
        => rating is > 0 and <= 10 ? Math.Round(rating.Value, 1).ToString("0.0", CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// Formats a critic rating (0-100) for display, e.g. "85%".
    /// </summary>
    /// <param name="rating">Rating.</param>
    /// <returns>Text and whether it is fresh (60% or more), or null when missing.</returns>
    public static (string Text, bool Fresh)? RottenTomatoes(float? rating)
        => rating is >= 0 and <= 100
            ? (Math.Round(rating.Value).ToString(CultureInfo.InvariantCulture) + "%", rating.Value >= 60)
            : null;

    private static int CodecRank(MediaStream s)
    {
        var codec = (s.Codec ?? string.Empty).ToLowerInvariant();
        var profile = s.Profile ?? string.Empty;
        return codec switch
        {
            "truehd" => 9,
            "dts" when profile.Contains("MA", StringComparison.Ordinal) => 9,
            _ when codec.StartsWith("pcm", StringComparison.Ordinal) => 8,
            "flac" => 8,
            "dts" when profile.Contains("HD", StringComparison.Ordinal) => 7,
            "eac3" => 6,
            "dts" => 5,
            "ac3" => 4,
            "opus" => 3,
            "aac" => 2,
            _ => 1,
        };
    }

    [GeneratedRegex(@"\{edition-([^}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex EditionTag();

    [GeneratedRegex(@"(?<![0-9])(19|20)[0-9]{2}(?![0-9])")]
    private static partial Regex Year();

    // Tokens are matched between non-alphanumerics so "Movie_BluRay_1080p" and "Bluray-1080p" both work.
    [GeneratedRegex(@"(?<![a-z0-9])(blu-?ray|bdremux|bdrip|brrip|bd25|bd50|bdmv|remux)(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex BluRaySource();

    [GeneratedRegex(@"(?<![a-z0-9])(web-?dl|webrip|web)(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex WebSource();

    [GeneratedRegex(@"(?<![a-z0-9])(hdtv|pdtv|dsr)(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex HdtvSource();

    [GeneratedRegex(@"(?<![a-z0-9])(dvdrip|dvd5|dvd9|dvd)(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex DvdSource();
}
