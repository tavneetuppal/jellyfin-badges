using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// Applies templates to movie posters and restores originals.
/// Originals are backed up once and every render starts from the backup, so overlays never stack.
/// If a poster is replaced outside the plugin (metadata refresh, manual upload), the new poster becomes the original.
/// </summary>
public class OverlayService
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Bump when drawing changes so existing posters get redrawn. 2 = badges packed left without gaps.
    private const string LayoutVersion = "L2";

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<OverlayService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="logger">Logger.</param>
    public OverlayService(ILibraryManager libraryManager, IProviderManager providerManager, ILogger<OverlayService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    private static string WorkPath => Plugin.Instance!.WorkPath;

    private static string StatePath => Path.Combine(WorkPath, "state.json");

    private static string OriginalsPath => Path.Combine(WorkPath, "originals");

    /// <summary>
    /// Gets the folder for custom template zips.
    /// </summary>
    public static string TemplatesPath => Path.Combine(WorkPath, "templates");

    /// <summary>
    /// Loads all templates.
    /// </summary>
    /// <returns>Templates.</returns>
    public List<OverlayTemplate> LoadTemplates() => OverlayTemplate.LoadAll(TemplatesPath, _logger);

    /// <summary>
    /// Applies matching templates to every movie poster.
    /// </summary>
    /// <param name="progress">Progress (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public Task ApplyAllAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => ApplyAsync(Movies(), progress, true, cancellationToken);

    /// <summary>
    /// Applies matching templates to specific items (new or updated movies).
    /// </summary>
    /// <param name="itemIds">Item ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public Task ApplyItemsAsync(IEnumerable<Guid> itemIds, CancellationToken cancellationToken)
    {
        var items = itemIds
            .Select(id => _libraryManager.GetItemById(id))
            .Where(i => i is not null && i.GetBaseItemKind() == BaseItemKind.Movie && !i.IsVirtualItem)
            .Select(i => i!)
            .ToList();
        return items.Count == 0 ? Task.CompletedTask : ApplyAsync(items, new Progress<double>(), false, cancellationToken);
    }

    private async Task ApplyAsync(List<BaseItem> movies, IProgress<double> progress, bool fullRun, CancellationToken cancellationToken)
    {
        await RunLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = Plugin.Instance!.Configuration;
            var disabled = new HashSet<string>(config.DisabledTemplates, StringComparer.OrdinalIgnoreCase);
            var templates = LoadTemplates().Where(t => !disabled.Contains(t.Name)).ToList();
            if (fullRun)
            {
                _logger.LogInformation("Poster Overlays: {Count} active templates: {Names}", templates.Count, string.Join(", ", templates.Select(t => t.Name)));
            }

            var state = LoadState();
            if (!string.IsNullOrWhiteSpace(config.OnlyTitlesContaining))
            {
                movies = movies.Where(m => m.Name?.Contains(config.OnlyTitlesContaining.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
                if (fullRun)
                {
                    _logger.LogInformation("Poster Overlays: test mode, only titles containing '{Filter}' ({Count} movies)", config.OnlyTitlesContaining, movies.Count);
                }
            }

            int done = 0, changed = 0, failed = 0;
            foreach (var item in movies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await ApplyAsync(item, templates, state, config.JpegQuality, cancellationToken).ConfigureAwait(false))
                    {
                        changed++;
                        SaveState(state);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    _logger.LogError(ex, "Poster Overlays: failed on {Name}", item.Name);
                }

                progress.Report(100.0 * ++done / Math.Max(1, movies.Count));
            }

            SaveState(state);
            if (fullRun || changed > 0 || failed > 0)
            {
                _logger.LogInformation("Poster Overlays: {Changed} posters updated, {Failed} failed, {Total} movies checked", changed, failed, movies.Count);
            }
        }
        finally
        {
            RunLock.Release();
        }
    }

    /// <summary>
    /// Puts every original poster back and forgets all state.
    /// </summary>
    /// <param name="progress">Progress (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public async Task RestoreAllAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await RunLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = LoadState();
            var ids = state.Keys.ToList();
            int done = 0, restored = 0;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = Guid.TryParse(id, out var guid) ? _libraryManager.GetItemById(guid) : null;
                var original = state[id].Original;
                try
                {
                    if (item is not null && original is not null && File.Exists(Path.Combine(OriginalsPath, original)))
                    {
                        await SaveAsync(item, File.ReadAllBytes(Path.Combine(OriginalsPath, original)), MimeFor(original), cancellationToken).ConfigureAwait(false);
                        restored++;
                        _logger.LogInformation("Poster Overlays: restored {Name}", item.Name);
                    }

                    state.Remove(id);
                    SaveState(state);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Poster Overlays: restore failed for {Id}", id);
                }

                progress.Report(100.0 * ++done / Math.Max(1, ids.Count));
            }

            _logger.LogInformation("Poster Overlays: restored {Count} original posters", restored);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<bool> ApplyAsync(BaseItem item, List<OverlayTemplate> templates, Dictionary<string, ItemState> state, int quality, CancellationToken ct)
    {
        var info = item.GetImageInfo(ImageType.Primary, 0);
        if (info is null || !info.IsLocalFile || !File.Exists(info.Path))
        {
            return false;
        }

        var id = item.Id.ToString("N");
        var facts = MediaFacts.For(item);
        var hits = templates.Where(t => MediaFacts.Matches(t, facts, _logger)).ToList();
        var signature = LayoutVersion + "|" + string.Join('|', hits.Select(t => t.Name + ":" + t.Hash));

        state.TryGetValue(id, out var st);
        var replacedOutside = st is not null && st.ImageTicks != info.DateModified.Ticks;
        if (st is not null && !replacedOutside && st.Signature == signature)
        {
            return false; // already up to date
        }

        if (st is null || replacedOutside)
        {
            // New poster for this item. If it's one the plugin drew (e.g. an upgraded file that became a new item
            // and picked up poster.jpg from the media folder), keep using the clean backup it came from.
            var marker = JpegMarker.Read(info.Path);
            string original;
            if (marker is not null)
            {
                if (!File.Exists(Path.Combine(OriginalsPath, marker)))
                {
                    _logger.LogWarning("Poster Overlays: {Name} already has overlays but its original backup {Backup} is missing; skipped. Refresh its images to get a clean poster.", item.Name, marker);
                    return false;
                }

                original = marker;
            }
            else if (hits.Count == 0)
            {
                // Clean poster and nothing to draw.
                return state.Remove(id);
            }
            else
            {
                var ext = Path.GetExtension(info.Path).ToLowerInvariant();
                original = id + (string.IsNullOrEmpty(ext) ? ".jpg" : ext);
                File.Copy(info.Path, Path.Combine(OriginalsPath, original), true);
            }

            st = new ItemState { Original = original };
            state[id] = st;
        }

        var originalPath = Path.Combine(OriginalsPath, st.Original!);
        if (hits.Count == 0)
        {
            // Item no longer matches anything (template disabled/removed or file upgraded): put the original back.
            await SaveAsync(item, File.ReadAllBytes(originalPath), MimeFor(st.Original!), ct).ConfigureAwait(false);
            state.Remove(id);
            _logger.LogInformation("Poster Overlays: {Name} -> original (no matching templates)", item.Name);
            return true;
        }

        var jpeg = JpegMarker.Add(OverlayRenderer.Render(File.ReadAllBytes(originalPath), hits, quality, _logger), st.Original!);
        await SaveAsync(item, jpeg, "image/jpeg", ct).ConfigureAwait(false);
        st.Signature = signature;
        st.ImageTicks = item.GetImageInfo(ImageType.Primary, 0)?.DateModified.Ticks ?? 0;
        _logger.LogInformation("Poster Overlays: {Name} -> {Templates}", item.Name, string.Join(", ", hits.Select(t => t.Name)));
        return true;
    }

    private async Task SaveAsync(BaseItem item, byte[] bytes, string mime, CancellationToken ct)
    {
        using var ms = new MemoryStream(bytes);
        await _providerManager.SaveImage(item, ms, mime, ImageType.Primary, null, ct).ConfigureAwait(false);
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ct).ConfigureAwait(false);
    }

    private List<BaseItem> Movies() => _libraryManager.GetItemList(new InternalItemsQuery
    {
        IncludeItemTypes = [BaseItemKind.Movie],
        Recursive = true,
        IsVirtualItem = false,
    }).ToList();

    private static string MimeFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    private Dictionary<string, ItemState> LoadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<Dictionary<string, ItemState>>(File.ReadAllText(StatePath)) ?? []
                : [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Poster Overlays: state.json is corrupt; starting fresh (originals folder is kept)");
            return [];
        }
    }

    private static void SaveState(Dictionary<string, ItemState> state)
    {
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tmp, StatePath, true);
    }

    /// <summary>
    /// What the plugin last did to an item's poster.
    /// </summary>
    public sealed class ItemState
    {
        /// <summary>Gets or sets the backup file name in the originals folder.</summary>
        public string? Original { get; set; }

        /// <summary>Gets or sets the applied templates (name:hash list).</summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary>Gets or sets the poster's DateModified ticks right after the plugin saved it.</summary>
        public long ImageTicks { get; set; }
    }
}
