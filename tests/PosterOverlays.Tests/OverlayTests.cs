using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Overlay;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace PosterOverlays.Tests;

public class OverlayTests
{
    private static readonly List<OverlayTemplate> Templates =
        OverlayTemplate.LoadAll(Path.Combine(Path.GetTempPath(), "no-custom-templates"), NullLogger.Instance);

    private static Dictionary<string, object> Facts(string res, bool dv, bool hdr, string codec, int ch, string path,
        bool atmos = false, bool dtsX = false, bool dtsHdMa = false, string source = "bluray") => new()
    {
        ["resolution"] = res, ["dolbyVision"] = dv, ["hdr"] = hdr, ["hdr10Plus"] = false,
        ["videoCodec"] = "hevc", ["audioCodec"] = codec, ["audioChannels"] = ch,
        ["atmos"] = atmos, ["dtsX"] = dtsX, ["dtsHdMa"] = dtsHdMa,
        ["source"] = source, ["filePath"] = path, ["title"] = "x", ["year"] = 2019,
    };

    private static string[] Hits(Dictionary<string, object> f) =>
        Templates.Where(t => MediaFacts.Matches(t, f, NullLogger.Instance)).Select(t => t.Name).ToArray();

    [Fact]
    public void LoadsAllBuiltInTemplates()
    {
        Assert.Equal(
            new[] { "1080p Full HD", "4K", "720p HD", "BluRay", "Dolby Atmos", "DoVi Top Left", "DTS HD MA", "DTS X", "HDR10 Top Left", "IMAX", "SD" }.OrderBy(n => n),
            Templates.Select(t => t.Name).OrderBy(n => n));
        Assert.All(Templates, t => Assert.NotEmpty(t.Icons));
    }

    [Fact]
    public void UhdDolbyVisionAtmosImax()
        => Assert.Equal(new[] { "4K", "Dolby Atmos", "DoVi Top Left", "IMAX" }, Hits(Facts("4k", true, true, "truehd", 8, "/m/Avengers Imax/a.mkv", atmos: true)));

    [Fact]
    public void HdHdr10DtsHdMa()
        => Assert.Equal(new[] { "BluRay", "DTS HD MA", "HDR10 Top Left" }, Hits(Facts("1080", false, true, "dts", 6, "/m/x.mkv", dtsHdMa: true)));

    [Fact]
    public void AtmosOn51TrueHd()
        => Assert.Equal(new[] { "BluRay", "Dolby Atmos" }, Hits(Facts("1080", false, false, "truehd", 6, "/m/The Batman.mkv", atmos: true)));

    [Fact]
    public void DtsXDoesNotAlsoShowDtsHdMa()
        => Assert.Equal(new[] { "BluRay", "DTS X" }, Hits(Facts("1080", false, false, "dts", 8, "/m/x.mkv", dtsX: true, dtsHdMa: true)));

    [Fact]
    public void ImaxPacksRightAfterResolution()
    {
        var used = Templates.Where(t => t.Name is "BluRay" or "IMAX").ToList();
        var p = OverlayRenderer.PackRows(used);
        var bluray = used.Single(t => t.Name == "BluRay");
        var imax = used.Single(t => t.Name == "IMAX");
        Assert.Equal(11.302785, p[bluray].X, 3);
        // BluRay tile: x 11.3, width 304 -> IMAX tile (designed at x 637) should start at 11.3 + 304 + 9
        Assert.Equal(11.302785 + 304 + 9, p[imax].X, 3);
    }

    [Fact]
    public void FullTopRowKeepsDesignedOrder()
    {
        var used = Templates.Where(t => t.Name is "4K" or "DoVi Top Left" or "IMAX" or "Dolby Atmos").ToList();
        var o = OverlayRenderer.PackRows(used);
        Assert.Equal(o[used.Single(t => t.Name == "4K")].BoxX, o[used.Single(t => t.Name == "4K")].X, 3);
        Assert.Equal(o[used.Single(t => t.Name == "Dolby Atmos")].BoxX, o[used.Single(t => t.Name == "Dolby Atmos")].X, 3); // bottom row stays put
        Assert.True(Math.Abs(o[used.Single(t => t.Name == "IMAX")].X - 637) < 1); // already packed in the design
    }

    [Fact]
    public void TwentyPercentLargerTopRowStillFits()
    {
        var used = Templates.Where(t => t.Name is "4K" or "DoVi Top Left" or "IMAX" or "Dolby Atmos").ToList();
        var o = OverlayRenderer.PackRows(used, 1.2);
        var imax = o[used.Single(t => t.Name == "IMAX")];
        Assert.True(imax.X + imax.Width <= 1000 - 11, $"top row ends at {imax.X + imax.Width}");
        Assert.Equal(304 * 1.2, o[used.Single(t => t.Name == "4K")].Width, 3);
        var atmos = o[used.Single(t => t.Name == "Dolby Atmos")];
        Assert.False(atmos.TopRow);
        Assert.Equal(1430 + 53, atmos.AnchorY, 3);   // bottom row grows upwards from the same bottom edge
    }

    [Fact]
    public void PlainSdGetsSdBadge()
        => Assert.Equal(new[] { "SD" }, Hits(Facts("sd", false, false, "aac", 2, "/m/x.mkv", source: "dvd")));

    [Theory]
    [InlineData("bluray", "BluRay")]
    [InlineData("web", "1080p Full HD")]
    [InlineData("hdtv", "1080p Full HD")]
    [InlineData("unknown", "1080p Full HD")]
    public void BluRayOnlyForRealBluRaySources(string source, string expected)
        => Assert.Equal(new[] { expected }, Hits(Facts("1080", false, false, "aac", 2, "/m/x.mkv", source: source)));

    [Fact]
    public void SevenTwentyGetsHdBadge()
        => Assert.Equal(new[] { "720p HD" }, Hits(Facts("720", false, false, "aac", 2, "/m/x.mkv", source: "bluray")));

    [Fact]
    public void RendersEveryTemplateWithoutErrors()
    {
        var poster = Gradient(1000, 1500);
        var logger = new CollectingLogger();
        foreach (var set in new[] { new[] { "4K", "DoVi Top Left", "IMAX", "Dolby Atmos" }, new[] { "BluRay", "HDR10 Top Left", "DTS HD MA" }, new[] { "DTS X" }, new[] { "BluRay", "IMAX" }, new[] { "4K", "IMAX", "Dolby Atmos" } })
        {
            var jpeg = OverlayRenderer.Render(poster, Templates.Where(t => set.Contains(t.Name)).ToList(), 92, logger);
            using var bmp = SKBitmap.Decode(jpeg);
            Assert.Equal(1000, bmp.Width);
            Assert.Equal(1500, bmp.Height);
            File.WriteAllBytes(Path.Combine(OutDir, string.Join("_", set).Replace(' ', '-') + ".jpg"), jpeg);
        }

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void ScalesToSmallerPosters()
    {
        var jpeg = OverlayRenderer.Render(Gradient(680, 1000), Templates.Where(t => t.Name == "4K").ToList(), 92, NullLogger.Instance);
        using var bmp = SKBitmap.Decode(jpeg);
        Assert.Equal(680, bmp.Width);
        // the 4K tile sits top-left: pixel inside it must be darker than the untouched poster
        Assert.True(bmp.GetPixel(100, 25).Red < SKBitmap.Decode(Gradient(680, 1000)).GetPixel(100, 25).Red);
    }

    [Fact]
    public void MarkerRoundTripsAndKeepsJpegValid()
    {
        var jpeg = OverlayRenderer.Render(Gradient(300, 450), Templates.Where(t => t.Name == "4K").ToList(), 92, NullLogger.Instance);
        Assert.Null(JpegMarker.Read(jpeg));

        var marked = JpegMarker.Add(jpeg, "abc123.jpg");
        Assert.Equal("abc123.jpg", JpegMarker.Read(marked));
        using var bmp = SKBitmap.Decode(marked);
        Assert.Equal(300, bmp.Width);

        var path = Path.Combine(OutDir, "marked.jpg");
        File.WriteAllBytes(path, marked);
        Assert.Equal("abc123.jpg", JpegMarker.Read(path));
    }

    [Fact]
    public void MarkerIgnoresOtherImagesAndPathTricks()
    {
        Assert.Null(JpegMarker.Read(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        Assert.Equal("x.jpg", JpegMarker.Read(JpegMarker.Add(Gradient(10, 10), "../../x.jpg")));
    }

    private static string OutDir => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "poster-overlay-tests")).FullName;

    private static byte[] Gradient(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp))
        using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), new[] { new SKColor(150, 90, 60), new SKColor(50, 160, 180) }, SKShaderTileMode.Clamp) })
        {
            c.DrawRect(0, 0, w, h, p);
        }

        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
    }
}

internal sealed class CollectingLogger : Microsoft.Extensions.Logging.ILogger
{
    public List<string> Warnings { get; } = new();

    public System.IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter)
    {
        if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception) + " " + exception);
        }
    }
}
