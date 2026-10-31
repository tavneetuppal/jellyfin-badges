using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// Plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the names of templates that should not be applied.
    /// </summary>
    public string[] DisabledTemplates { get; set; } = [];

    /// <summary>
    /// Gets or sets the JPEG quality used for overlaid posters (1-100).
    /// </summary>
    public int JpegQuality { get; set; } = 92;

    /// <summary>
    /// Gets or sets a title filter for trying the plugin out: when set, only movies whose title contains this text are processed.
    /// </summary>
    public string OnlyTitlesContaining { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether movies without an Atmos/DTS badge get an audio label (e.g. "DOLBY | TrueHD 7.1").
    /// </summary>
    public bool ShowAudioLabel { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the edition (Director's Cut, Extended...) from the file name is shown under the top row.
    /// </summary>
    public bool ShowEdition { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the IMDb (community) rating is shown bottom-right.
    /// </summary>
    public bool ShowImdbRating { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the Rotten Tomatoes (critic) rating is shown bottom-right.
    /// </summary>
    public bool ShowRottenTomatoes { get; set; } = true;
}
