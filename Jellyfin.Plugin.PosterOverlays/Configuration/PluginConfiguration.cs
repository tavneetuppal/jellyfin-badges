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
}
