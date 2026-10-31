using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.Overlay;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.PosterOverlays.Tasks;

/// <summary>
/// Puts the original posters back.
/// </summary>
public class RestoreOriginalsTask : IScheduledTask
{
    private readonly OverlayService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="RestoreOriginalsTask"/> class.
    /// </summary>
    /// <param name="service">Overlay service.</param>
    public RestoreOriginalsTask(OverlayService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public string Name => "Restore original posters";

    /// <inheritdoc />
    public string Key => "PosterOverlaysRestore";

    /// <inheritdoc />
    public string Description => "Removes all overlays by putting the backed-up original posters back. Disable the daily apply task first or it will re-add them.";

    /// <inheritdoc />
    public string Category => "Poster Overlays";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.RestoreAllAsync(progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
