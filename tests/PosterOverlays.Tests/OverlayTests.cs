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

    private static Dictionary<string, object> Facts(string res, bool dv, bool hdr, string codec, int ch, string path) => new()
    {
        ["resolution"] = res, ["dolbyVision"] = dv, ["hdr"] = hdr, ["hdr10Plus"] = false,
        ["videoCodec"] = "hevc", ["audioCodec"] = codec, ["audioChannels"] = ch,
        ["filePath"] = path, ["title"] = "x", ["year"] = 2019,
    };

    private static string[] Hits(Dictionary<string, object> f) =>
        Templates.Where(t => MediaFacts.Matches(t, f, NullLogger.Instance)).Select(t => t.Name).ToArray();

    [Fact]
    public void LoadsAllBuiltInTemplates()
    {
        Assert.Equal(
            new[] { "4K", "BluRay", "Dolby Atmos", "DoVi Top Left", "DTS HD MA", "DTS X", "HDR10 Top Left", "IMAX" }.OrderBy(n => n),
            Templates.Select(t => t.Name).OrderBy(n => n));
        Assert.All(Templates, t => Assert.NotEmpty(t.Icons));
    }

    [Fact]
    public void UhdDolbyVisionAtmosImax()
        => Assert.Equal(new[] { "4K", "Dolby Atmos", "DoVi Top Left", "IMAX" }, Hits(Facts("4k", true, true, "truehd", 8, "/m/Avengers Imax/a.mkv")));

    [Fact]
    public void HdHdr10DtsHdMa()
        => Assert.Equal(new[] { "BluRay", "DTS HD MA", "HDR10 Top Left" }, Hits(Facts("1080", false, true, "dts", 6, "/m/x.mkv")));

    [Fact]
    public void DtsXUsesPlexDcaAlias()
        => Assert.Contains("DTS X", Hits(Facts("1080", false, false, "DTS", 8, "/m/x.mkv")));

    [Fact]
    public void PlainSdMatchesNothing()
        => Assert.Empty(Hits(Facts("sd", false, false, "aac", 2, "/m/x.mkv")));

    [Fact]
    public void RendersEveryTemplateWithoutErrors()
    {
        var poster = Gradient(1000, 1500);
        var logger = new CollectingLogger();
        foreach (var set in new[] { new[] { "4K", "DoVi Top Left", "IMAX", "Dolby Atmos" }, new[] { "BluRay", "HDR10 Top Left", "DTS HD MA" }, new[] { "DTS X" } })
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
