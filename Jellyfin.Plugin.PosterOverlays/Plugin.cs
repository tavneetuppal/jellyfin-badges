using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// Burns Agregarr overlay templates (quality badges) into movie posters.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Plugin id. Must match manifest.json.
    /// </summary>
    public static readonly Guid PluginId = Guid.Parse("206aa7e3-be50-4e58-b06c-5acbd8778d94");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        WorkPath = Path.Combine(applicationPaths.PluginConfigurationsPath, "PosterOverlays");
        Directory.CreateDirectory(Path.Combine(WorkPath, "templates"));
        Directory.CreateDirectory(Path.Combine(WorkPath, "originals"));
    }

    /// <summary>
    /// Gets the running plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the folder holding user templates, original poster backups and state.
    /// </summary>
    public string WorkPath { get; }

    /// <inheritdoc />
    public override string Name => "Poster Overlays";

    /// <inheritdoc />
    public override string Description => "Burns quality badges (4K, Dolby Vision, Atmos, IMAX...) into movie posters so they show in every client.";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        ];
    }
}
