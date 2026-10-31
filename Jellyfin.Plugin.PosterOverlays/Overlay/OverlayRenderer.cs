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
    {
        using var decoded = SKBitmap.Decode(poster) ?? throw new InvalidDataException("Poster could not be decoded");
        using var bitmap = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawBitmap(decoded, 0, 0);
            var offsets = PackRows(templates);
            foreach (var t in templates)
            {
                Draw(canvas, bitmap.Width, bitmap.Height, t, offsets[t], logger);
            }

            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(jpegQuality, 1, 100));
        return data.ToArray();
    }

    /// <summary>
    /// Slides badges left so a row has no holes where a badge didn't apply (e.g. IMAX right after 4K when there's no HDR).
    /// Badges are grouped into a top and bottom row by their designed position and keep their designed left-to-right order.
    /// </summary>
    /// <param name="templates">Templates being drawn.</param>
    /// <returns>Horizontal shift per template, in template units.</returns>
    internal static Dictionary<OverlayTemplate, double> PackRows(IReadOnlyList<OverlayTemplate> templates)
    {
        var boxes = templates.Select(t => (Template: t, Box: AnchorBox(t))).ToList();
        var offsets = templates.ToDictionary(t => t, _ => 0.0);
        foreach (var row in boxes.Where(b => b.Box is not null).GroupBy(b => b.Box!.Value.MidY < b.Box.Value.CanvasHeight / 2))
        {
            var ordered = row.OrderBy(b => b.Box!.Value.X).ThenBy(b => b.Template.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var x = ordered[0].Box!.Value.X;
            foreach (var (template, box) in ordered)
            {
                offsets[template] = x - box!.Value.X;
                x += box.Value.Width + BadgeGap;
            }
        }

        return offsets;
    }

    // Gap between badges, in template units (matches the spacing in the Agregarr designs).
    private const double BadgeGap = 9;

    // The badge's background tile defines its footprint; icons can overhang (transparent padding in the SVG).
    private static (double X, double Width, double MidY, double CanvasHeight)? AnchorBox(OverlayTemplate t)
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
        return (left, right - left, (top + bottom) / 2, Num(td, "height", 1500));
    }

    private static void Draw(SKCanvas canvas, int w, int h, OverlayTemplate t, double dx, ILogger logger)
    {
        var td = t.Root.GetProperty("templateData");
        var sx = w / Num(td, "width", 1000);
        var sy = h / Num(td, "height", 1500);
        var elements = td.GetProperty("elements").EnumerateArray().OrderBy(e => Num(e, "layerOrder", 0));
        foreach (var el in elements)
        {
            var p = el.TryGetProperty("properties", out var props) ? props : default;
            var rect = SKRect.Create(
                (float)((Num(el, "x", 0) + dx) * sx),
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
