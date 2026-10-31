using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SkiaSharp;
using Svg.Skia;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// Draws the built-in badges in the same style as the Agregarr templates (black 35% tiles, 1000x1500 layout units).
/// Audio label: bottom-left. Edition: under the top row. Ratings: bottom-right, side by side.
/// </summary>
internal static class ExtraBadgeRenderer
{
    private const double Margin = 11.3;
    private const double TopY = 6.4;
    private const double BottomY = 1430;
    private const double TileH = 53;
    private const double Radius = 15;
    private static readonly SKColor TileColor = SKColors.Black.WithAlpha(89);

    private static readonly Lazy<SKTypeface> Bold = new(() => LoadFont("DejaVuSans-Bold.ttf"));
    private static readonly Lazy<SKTypeface> Regular = new(() => LoadFont("DejaVuSans.ttf"));
    private static readonly Lazy<SKPicture?> ImdbLogo = new(() => LoadSvg("imdb.svg"));
    private static readonly Lazy<SKPicture?> TomatoFresh = new(() => LoadSvg("rt_fresh.svg"));
    private static readonly Lazy<SKPicture?> TomatoRotten = new(() => LoadSvg("rt_rotten.svg"));

    public static void Draw(SKCanvas canvas, int w, int h, IReadOnlyList<ExtraBadge> badges, (bool TopRow, double BottomRight) rows)
    {
        var u = new Units(w / 1000.0, h / 1500.0);
        var bottomLeftEnd = rows.BottomRight;

        foreach (var b in badges.Where(b => b.Kind == ExtraBadgeKind.Audio))
        {
            var x = bottomLeftEnd > 0 ? bottomLeftEnd + OverlayRenderer.BadgeGap : Margin;
            DrawAudio(canvas, u, x, BottomY, b);
            bottomLeftEnd = x + 304;
        }

        foreach (var b in badges.Where(b => b.Kind == ExtraBadgeKind.Edition))
        {
            DrawLabel(canvas, u, Margin, rows.TopRow ? TopY + TileH + OverlayRenderer.BadgeGap : TopY, 44, b.Text, 24);
        }

        // Ratings: right-aligned, IMDb then Rotten Tomatoes; move up a row if they would hit the bottom-left badges.
        var ratings = badges
            .Where(b => b.Kind is ExtraBadgeKind.Imdb or ExtraBadgeKind.RottenTomatoes)
            .OrderBy(b => b.Kind == ExtraBadgeKind.Imdb ? 0 : 1)
            .ToList();
        if (ratings.Count == 0)
        {
            return;
        }

        var widths = ratings.Select(b => RatingWidth(b)).ToList();
        var total = widths.Sum() + (OverlayRenderer.BadgeGap * (ratings.Count - 1));
        var left = 1000 - Margin - total;
        var y = bottomLeftEnd > 0 && left < bottomLeftEnd + OverlayRenderer.BadgeGap ? BottomY - TileH - OverlayRenderer.BadgeGap : BottomY;
        for (var i = 0; i < ratings.Count; i++)
        {
            DrawRating(canvas, u, left, y, widths[i], ratings[i]);
            left += widths[i] + OverlayRenderer.BadgeGap;
        }
    }

    private static void DrawAudio(SKCanvas canvas, Units u, double x, double y, ExtraBadge b)
    {
        Tile(canvas, u, x, y, 304, TileH);
        using var brand = Font(Bold.Value, 27, u);
        using var detail = Font(Regular.Value, 27, u);
        const double sep = 14;
        var bw = brand.MeasureText(b.Text) / u.S;
        var dw = b.Detail.Length > 0 ? detail.MeasureText(b.Detail) / u.S : 0;
        var start = x + ((304 - (bw + (dw > 0 ? sep + dw : 0))) / 2);
        var mid = y + (TileH / 2);
        Text(canvas, u, b.Text, start, mid, brand, SKColors.White);
        if (dw > 0)
        {
            using var line = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(140), StrokeWidth = (float)(2 * u.S) };
            var lx = u.X(start + bw + (sep / 2));
            canvas.DrawLine(lx, u.Y(y + 14), lx, u.Y(y + TileH - 14), line);
            Text(canvas, u, b.Detail, start + bw + sep, mid, detail, SKColors.White.WithAlpha(225));
        }
    }

    private static void DrawLabel(SKCanvas canvas, Units u, double x, double y, double tileH, string text, double size)
    {
        using var font = Font(Bold.Value, size, u);
        var tw = font.MeasureText(text) / u.S;
        Tile(canvas, u, x, y, tw + 44, tileH);
        Text(canvas, u, text, x + 22, y + (tileH / 2), font, SKColors.White);
    }

    private static double RatingWidth(ExtraBadge b)
    {
        using var font = Font(Bold.Value, 30, new Units(1, 1));
        return 14 + IconWidth(b) + 12 + font.MeasureText(b.Text) + 16;
    }

    private static double IconWidth(ExtraBadge b)
    {
        var (pic, height) = Icon(b);
        return pic is null ? 0 : height * pic.CullRect.Width / pic.CullRect.Height;
    }

    private static (SKPicture? Picture, double Height) Icon(ExtraBadge b) => b.Kind == ExtraBadgeKind.Imdb
        ? (ImdbLogo.Value, 30)
        : (b.Fresh ? TomatoFresh.Value : TomatoRotten.Value, 36);

    private static void DrawRating(SKCanvas canvas, Units u, double x, double y, double width, ExtraBadge b)
    {
        Tile(canvas, u, x, y, width, TileH);
        var (pic, ih) = Icon(b);
        var iw = IconWidth(b);
        if (pic is not null)
        {
            canvas.Save();
            canvas.Translate(u.X(x + 14), u.Y(y + ((TileH - ih) / 2)));
            canvas.Scale((float)(iw * u.S / pic.CullRect.Width), (float)(ih * u.S / pic.CullRect.Height));
            canvas.Translate(-pic.CullRect.Left, -pic.CullRect.Top);
            canvas.DrawPicture(pic);
            canvas.Restore();
        }

        using var font = Font(Bold.Value, 30, u);
        Text(canvas, u, b.Text, x + 14 + iw + 12, y + (TileH / 2), font, SKColors.White);
    }

    private static void Tile(SKCanvas canvas, Units u, double x, double y, double w, double h)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = TileColor };
        var r = (float)(Radius * u.S);
        canvas.DrawRoundRect(SKRect.Create(u.X(x), u.Y(y), (float)(w * u.S), (float)(h * u.S)), r, r, paint);
    }

    private static void Text(SKCanvas canvas, Units u, string text, double x, double midY, SKFont font, SKColor color)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        var m = font.Metrics;
        var baseline = u.Y(midY) - ((m.Ascent + m.Descent) / 2);
        canvas.DrawText(text, u.X(x), baseline, SKTextAlign.Left, font, paint);
    }

    private static SKFont Font(SKTypeface face, double size, Units u) => new(face, (float)(size * u.S)) { Subpixel = true };

    private static SKTypeface LoadFont(string name)
    {
        using var s = Resource(name);
        return (s is null ? null : SKTypeface.FromStream(s)) ?? SKTypeface.Default;
    }

    private static SKPicture? LoadSvg(string name)
    {
        using var s = Resource(name);
        if (s is null)
        {
            return null;
        }

        var svg = new SKSvg(); // keep alive: the picture belongs to it
        return svg.Load(s);
    }

    private static Stream? Resource(string name)
        => Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(ExtraBadgeRenderer).Namespace!.Replace(".Overlay", ".Badges", StringComparison.Ordinal) + "." + name);

    // Layout units (1000x1500) -> pixels. S is the uniform size scale (posters are 2:3, so X and Y scales match).
    private readonly record struct Units(double Sx, double Sy)
    {
        public double S => Sx;

        public float X(double v) => (float)(v * Sx);

        public float Y(double v) => (float)(v * Sy);
    }
}
