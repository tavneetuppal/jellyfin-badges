using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Svg.Skia;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// Draws template elements (tiles and icons) onto a poster, scaled from the template canvas to the poster size.
/// </summary>
public static class OverlayRenderer
{
    /// <summary>
    /// Renders templates onto a poster.
    /// </summary>
    /// <param name="poster">Encoded source poster.</param>
    /// <param name="templates">Templates to draw, in order.</param>
    /// <param name="jpegQuality">JPEG quality.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>Encoded JPEG.</returns>
    public static byte[] Render(byte[] poster, IReadOnlyList<OverlayTemplate> templates, int jpegQuality, ILogger logger)
        => Render(poster, templates, [], jpegQuality, logger);

    /// <summary>
    /// Renders templates and built-in badges onto a poster.
    /// </summary>
    /// <param name="poster">Encoded source poster.</param>
    /// <param name="templates">Templates to draw, in order.</param>
    /// <param name="extras">Built-in badges (audio label, edition, ratings).</param>
    /// <param name="jpegQuality">JPEG quality.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>Encoded JPEG.</returns>
    public static byte[] Render(byte[] poster, IReadOnlyList<OverlayTemplate> templates, IReadOnlyList<ExtraBadge> extras, int jpegQuality, ILogger logger)
        => Render(poster, templates, extras, jpegQuality, 1.0, logger);

    /// <summary>
    /// Renders templates and built-in badges onto a poster at a given badge size.
    /// </summary>
    /// <param name="poster">Encoded source poster.</param>
    /// <param name="templates">Templates to draw, in order.</param>
    /// <param name="extras">Built-in badges (audio label, edition, ratings).</param>
    /// <param name="jpegQuality">JPEG quality.</param>
    /// <param name="scale">Badge size relative to the design (1.2 = 20% larger). Margins stay the same.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>Encoded JPEG.</returns>
    public static byte[] Render(byte[] poster, IReadOnlyList<OverlayTemplate> templates, IReadOnlyList<ExtraBadge> extras, int jpegQuality, double scale, ILogger logger)
    {
        using var decoded = SKBitmap.Decode(poster) ?? throw new InvalidDataException("Poster could not be decoded");
        using var bitmap = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawBitmap(decoded, 0, 0);
            var placements = PackRows(templates, scale);
            foreach (var t in templates)
            {
                Draw(canvas, bitmap.Width, bitmap.Height, t, placements[t], logger);
            }

            if (extras.Count > 0)
            {
                ExtraBadgeRenderer.Draw(canvas, bitmap.Width, bitmap.Height, extras, RowExtents(templates, placements), scale);
            }

            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(jpegQuality, 1, 100));
        return data.ToArray();
    }

    /// <summary>
    /// Where each badge goes: scaled around its row's corner (top-left for the top row, bottom-left for the bottom row)
    /// and slid left so a row has no holes where a badge didn't apply (e.g. IMAX right after 4K when there's no HDR).
    /// Badges keep their designed left-to-right order; the row starts at its designed margin.
    /// </summary>
    /// <param name="templates">Templates being drawn.</param>
    /// <param name="scale">Badge size relative to the design.</param>
    /// <returns>Placement per template, in template units.</returns>
    internal static Dictionary<OverlayTemplate, Placement> PackRows(IReadOnlyList<OverlayTemplate> templates, double scale = 1.0)
    {
        var placements = templates.ToDictionary(t => t, _ => new Placement(0, 0, 0, 0, 1.0, false));
        var boxes = templates.Select(t => (Template: t, Box: AnchorBox(t))).ToList();
        foreach (var row in boxes.Where(b => b.Box is not null).GroupBy(b => b.Box!.Value.MidY < b.Box.Value.CanvasHeight / 2))
        {
            var ordered = row.OrderBy(b => b.Box!.Value.X).ThenBy(b => b.Template.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var x = ordered[0].Box!.Value.X;
            foreach (var (template, box) in ordered)
            {
                var b = box!.Value;
                var anchorY = row.Key ? b.Y : b.Y + b.Height;   // top row grows down, bottom row grows up
                placements[template] = new Placement(b.X, anchorY, x, b.Width * scale, scale, row.Key);
                x += (b.Width * scale) + BadgeGap;
            }
        }

        return placements;
    }

    /// <summary>
    /// Where a template is drawn: its anchor (BoxX, AnchorY) moves to (X, AnchorY) and the template scales around it.
    /// </summary>
    /// <param name="BoxX">Designed left edge of the badge.</param>
    /// <param name="AnchorY">Designed top (top row) or bottom (bottom row) edge.</param>
    /// <param name="X">Packed left edge.</param>
    /// <param name="Width">Drawn width.</param>
    /// <param name="Scale">Size factor.</param>
    /// <param name="TopRow">Whether the badge is in the top row.</param>
    internal readonly record struct Placement(double BoxX, double AnchorY, double X, double Width, double Scale, bool TopRow);

    /// <summary>
    /// Gap between badges, in template units (matches the spacing in the Agregarr designs).
    /// </summary>
    internal const double BadgeGap = 9;

    /// <summary>
    /// Where the template rows end after packing, so built-in badges can be placed around them.
    /// </summary>
    /// <param name="templates">Templates being drawn.</param>
    /// <param name="placements">Placements from <see cref="PackRows"/>.</param>
    /// <returns>Whether the top row has badges, and the right edge of the bottom row (template units, 0 when empty).</returns>
    internal static (bool TopRow, double BottomRight) RowExtents(IReadOnlyList<OverlayTemplate> templates, Dictionary<OverlayTemplate, Placement> placements)
    {
        bool top = false;
        double bottomRight = 0;
        foreach (var t in templates)
        {
            if (AnchorBox(t) is null)
            {
                continue;
            }

            var p = placements[t];
            if (p.TopRow)
            {
                top = true;
            }
            else
            {
                bottomRight = Math.Max(bottomRight, p.X + p.Width);
            }
        }

        return (top, bottomRight);
    }

    // The badge's background tile defines its footprint; icons can overhang (transparent padding in the SVG).
    private static (double X, double Y, double Width, double Height, double MidY, double CanvasHeight)? AnchorBox(OverlayTemplate t)
    {
        var td = t.Root.GetProperty("templateData");
        var elements = td.GetProperty("elements").EnumerateArray().ToList();
        var anchors = elements.Where(e => Str(e, "type") == "tile").ToList();
        if (anchors.Count == 0)
        {
            anchors = elements;
        }

        if (anchors.Count == 0)
        {
            return null;
        }

        var left = anchors.Min(e => Num(e, "x", 0));
        var right = anchors.Max(e => Num(e, "x", 0) + Num(e, "width", 0));
        var top = anchors.Min(e => Num(e, "y", 0));
        var bottom = anchors.Max(e => Num(e, "y", 0) + Num(e, "height", 0));
        return (left, top, right - left, bottom - top, (top + bottom) / 2, Num(td, "height", 1500));
    }

    private static void Draw(SKCanvas canvas, int w, int h, OverlayTemplate t, Placement place, ILogger logger)
    {
        var td = t.Root.GetProperty("templateData");
        var sx = w / Num(td, "width", 1000);
        var sy = h / Num(td, "height", 1500);

        // Move the anchor to its packed spot and scale the whole badge around it.
        canvas.Save();
        canvas.Translate((float)(place.X * sx), (float)(place.AnchorY * sy));
        canvas.Scale((float)place.Scale);
        canvas.Translate((float)(-place.BoxX * sx), (float)(-place.AnchorY * sy));

        var elements = td.GetProperty("elements").EnumerateArray().OrderBy(e => Num(e, "layerOrder", 0));
        foreach (var el in elements)
        {
            var p = el.TryGetProperty("properties", out var props) ? props : default;
            var rect = SKRect.Create(
                (float)(Num(el, "x", 0) * sx),
                (float)(Num(el, "y", 0) * sy),
                (float)(Num(el, "width", 0) * sx),
                (float)(Num(el, "height", 0) * sy));
            var rotation = (float)Num(el, "rotation", 0);
            canvas.Save();
            if (rotation != 0)
            {
                canvas.RotateDegrees(rotation, rect.MidX, rect.MidY);
            }

            switch (Str(el, "type"))
            {
                case "tile":
                    DrawTile(canvas, rect, p, (float)sx);
                    break;
                case "svg":
                    DrawIcon(canvas, rect, p, t, logger);
                    break;
                default:
                    logger.LogWarning("Template {Template}: element type '{Type}' is not supported, skipped", t.Name, Str(el, "type"));
                    break;
            }

            canvas.Restore();
        }

        canvas.Restore();
    }

    private static void DrawTile(SKCanvas canvas, SKRect rect, JsonElement p, float scale)
    {
        var radius = (float)Num(p, "borderRadiusTopLeft", 0) * scale;
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = Color(Str(p, "fillColor") ?? "#000000", Num(p, "fillOpacity", 100)),
        };
        canvas.DrawRoundRect(rect, radius, radius, fill);

        var border = (float)Num(p, "borderWidth", 0) * scale;
        if (border > 0)
        {
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = border,
                Color = Color(Str(p, "borderColor") ?? "#FFFFFF", 100),
            };
            canvas.DrawRoundRect(rect, radius, radius, stroke);
        }
    }

    private static void DrawIcon(SKCanvas canvas, SKRect box, JsonElement p, OverlayTemplate t, ILogger logger)
    {
        var file = Path.GetFileName(Str(p, "iconPath") ?? string.Empty);
        if (!t.Icons.TryGetValue(file, out var bytes))
        {
            logger.LogWarning("Template {Template}: icon {Icon} missing from template zip", t.Name, file);
            return;
        }

        var opacity = (byte)Math.Round(255 * Math.Clamp(Num(p, "opacity", 100), 0, 100) / 100);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(opacity) };
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("grayscale", out var g) && g.ValueKind == JsonValueKind.True)
        {
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(
            [
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0, 0, 0, 1, 0,
            ]);
        }

        if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            using var png = SKBitmap.Decode(bytes);
            if (png is not null)
            {
                using var img = SKImage.FromBitmap(png);
                canvas.DrawImage(img, Fit(png.Width, png.Height, box), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
            }

            return;
        }

        using var svg = new SKSvg();
        SKPicture? picture;
        try
        {
            using var ms = new MemoryStream(bytes);
            picture = svg.Load(ms);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Template {Template}: icon {Icon} could not be parsed", t.Name, file);
            return;
        }

        if (picture is null || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
        {
            logger.LogWarning("Template {Template}: icon {Icon} is empty", t.Name, file);
            return;
        }

        // Scale the SVG to fit inside the element box (keep aspect ratio, centered).
        var src = picture.CullRect;
        var dst = Fit(src.Width, src.Height, box);
        canvas.Save();
        canvas.Translate(dst.Left, dst.Top);
        canvas.Scale(dst.Width / src.Width, dst.Height / src.Height);
        canvas.Translate(-src.Left, -src.Top);
        canvas.SaveLayer(paint);
        canvas.DrawPicture(picture);
        canvas.Restore();
        canvas.Restore();
    }

    private static SKRect Fit(float srcW, float srcH, SKRect box)
    {
        var scale = Math.Min(box.Width / srcW, box.Height / srcH);
        var w = srcW * scale;
        var h = srcH * scale;
        return SKRect.Create(box.MidX - (w / 2), box.MidY - (h / 2), w, h);
    }

    private static SKColor Color(string hex, double opacityPct)
    {
        if (!SKColor.TryParse(hex, out var c))
        {
            c = SKColors.Black;
        }

        return c.WithAlpha((byte)Math.Round(255 * Math.Clamp(opacityPct, 0, 100) / 100));
    }

    private static double Num(JsonElement e, string name, double fallback)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return fallback;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => fallback,
        };
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
