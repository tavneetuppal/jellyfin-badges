using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>Kind of built-in badge.</summary>
public enum ExtraBadgeKind
{
    /// <summary>Audio label for movies without an Atmos/DTS template badge (bottom-left).</summary>
    Audio,

    /// <summary>Edition name under the top row.</summary>
    Edition,

    /// <summary>IMDb (community) rating, bottom-right.</summary>
    Imdb,

    /// <summary>Rotten Tomatoes (critic) rating, bottom-right.</summary>
    RottenTomatoes,
}

/// <summary>
/// A built-in badge whose text comes from the item (not from an Agregarr template).
/// </summary>
/// <param name="Kind">Badge kind.</param>
/// <param name="Text">Main (bold) text.</param>
/// <param name="Detail">Secondary text (audio detail), or empty.</param>
/// <param name="Fresh">Rotten Tomatoes: fresh (true) or rotten (false).</param>
public sealed record ExtraBadge(ExtraBadgeKind Kind, string Text, string Detail = "", bool Fresh = true)
{
    /// <summary>Gets a stable key used to detect changes (e.g. a rating moving).</summary>
    public string Key => $"{Kind}:{Text}:{Detail}:{Fresh}";
}

/// <summary>
/// Decides which built-in badges an item gets.
/// </summary>
public static class ExtraBadges
{
    // Template condition fields that mean "this template is an audio badge".
    private static readonly HashSet<string> AudioFields = ["atmos", "dtsX", "dtsHdMa", "audioCodec", "audioChannels"];

    /// <summary>
    /// Builds the built-in badges for an item.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <param name="hits">Templates that matched the item.</param>
    /// <param name="config">Plugin settings.</param>
    /// <returns>Badges to draw.</returns>
    public static List<ExtraBadge> For(BaseItem item, IReadOnlyList<OverlayTemplate> hits, PluginConfiguration config)
        => For(item.Path, item.GetMediaStreams(), item.CommunityRating, item.CriticRating, hits, config);

    /// <summary>
    /// Builds the built-in badges for a show: audio from its representative episode, ratings from the show, no edition.
    /// </summary>
    /// <param name="media">Representative episode, or null.</param>
    /// <param name="show">The show.</param>
    /// <param name="hits">Templates that matched.</param>
    /// <param name="config">Plugin settings.</param>
    /// <returns>Badges to draw.</returns>
    public static List<ExtraBadge> ForShow(BaseItem? media, BaseItem show, IReadOnlyList<OverlayTemplate> hits, PluginConfiguration config)
        => For(null, media?.GetMediaStreams() ?? [], show.CommunityRating, show.CriticRating, hits, config);

    /// <summary>
    /// Builds the built-in badges from raw item data (testable without a library item).
    /// </summary>
    /// <param name="path">Media path.</param>
    /// <param name="streams">Media streams.</param>
    /// <param name="communityRating">Community rating (0-10).</param>
    /// <param name="criticRating">Critic rating (0-100).</param>
    /// <param name="hits">Templates that matched.</param>
    /// <param name="config">Plugin settings.</param>
    /// <returns>Badges to draw.</returns>
    public static List<ExtraBadge> For(
        string? path,
        IReadOnlyList<MediaBrowser.Model.Entities.MediaStream> streams,
        float? communityRating,
        float? criticRating,
        IReadOnlyList<OverlayTemplate> hits,
        PluginConfiguration config)
    {
        var badges = new List<ExtraBadge>();

        if (config.ShowAudioLabel && !hits.Any(IsAudioTemplate) && BadgeInfo.Audio(streams) is { } audio)
        {
            badges.Add(new ExtraBadge(ExtraBadgeKind.Audio, audio.Brand, audio.Detail));
        }

        if (config.ShowEdition && BadgeInfo.Edition(path) is { } edition)
        {
            badges.Add(new ExtraBadge(ExtraBadgeKind.Edition, edition));
        }

        if (config.ShowImdbRating && BadgeInfo.Imdb(communityRating) is { } imdb)
        {
            badges.Add(new ExtraBadge(ExtraBadgeKind.Imdb, imdb));
        }

        if (config.ShowRottenTomatoes && BadgeInfo.RottenTomatoes(criticRating) is { } rt)
        {
            badges.Add(new ExtraBadge(ExtraBadgeKind.RottenTomatoes, rt.Text, Fresh: rt.Fresh));
        }

        return badges;
    }

    private static bool IsAudioTemplate(OverlayTemplate t)
    {
        if (!t.Root.TryGetProperty("applicationCondition", out var c) || c.ValueKind != System.Text.Json.JsonValueKind.Object
            || !c.TryGetProperty("sections", out var sections) || sections.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        return sections.EnumerateArray()
            .Where(s => s.TryGetProperty("rules", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Array)
            .SelectMany(s => s.GetProperty("rules").EnumerateArray())
            .Any(r => r.TryGetProperty("field", out var f) && AudioFields.Contains(f.GetString() ?? string.Empty));
    }
}
