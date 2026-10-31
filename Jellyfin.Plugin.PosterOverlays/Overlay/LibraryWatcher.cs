using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// Re-applies overlays shortly after movies are added or updated (new media, file upgrades, poster changes),
/// instead of waiting for the daily task. Changes are batched so a library scan becomes one run.
/// The plugin's own poster saves also raise update events; those are no-ops because the item is already up to date.
/// </summary>
public sealed class LibraryWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    private readonly ILibraryManager _libraryManager;
    private readonly OverlayService _service;
    private readonly ILogger<LibraryWatcher> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Timer _timer;
    private int _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="service">Overlay service.</param>
    /// <param name="logger">Logger.</param>
    public LibraryWatcher(ILibraryManager libraryManager, OverlayService service, ILogger<LibraryWatcher> logger)
    {
        _libraryManager = libraryManager;
        _service = service;
        _logger = logger;
        _timer = new Timer(_ => _ = FlushAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Dispose();
        _stopping.Dispose();
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is null || e.Item.GetBaseItemKind() != BaseItemKind.Movie || e.Item.IsVirtualItem)
        {
            return;
        }

        _pending[e.Item.Id] = 0;
        _timer.Change(Delay, Timeout.InfiniteTimeSpan); // restart the quiet period
    }

    private async Task FlushAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            _timer.Change(Delay, Timeout.InfiniteTimeSpan); // a batch is still running; try again later
            return;
        }

        try
        {
            var ids = _pending.Keys.ToList();
            foreach (var id in ids)
            {
                _pending.TryRemove(id, out _);
            }

            if (ids.Count > 0)
            {
                await _service.ApplyItemsAsync(ids, _stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Poster Overlays: failed to process library changes");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
