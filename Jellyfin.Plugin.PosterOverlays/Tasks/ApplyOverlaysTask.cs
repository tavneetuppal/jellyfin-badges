using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.Overlay;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.PosterOverlays.Tasks;

/// <summary>
/// Applies overlay templates to movie posters.
/// </summary>
public class ApplyOverlaysTask : IScheduledTask
{
    private readonly OverlayService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyOverlaysTask"/> class.
    /// </summary>
    /// <param name="service">Overlay service.</param>
    public ApplyOverlaysTask(OverlayService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public string Name => "Apply poster overlays";

    /// <inheritdoc />
    public string Key => "PosterOverlaysApply";

    /// <inheritdoc />
    public string Description => "Adds quality badges to movie posters. New and upgraded movies are handled automatically; this catches anything missed. Only changed or new movies are re-drawn.";

    /// <inheritdoc />
    public string Category => "Poster Overlays";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.ApplyAllAsync(progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Daily at 3am; also run it from Dashboard > Scheduled Tasks after a library scan.
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
        };
    }
}
