using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.Overlay;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace PosterOverlays.Tests;

public class BadgeTests
{
    private static readonly List<OverlayTemplate> Templates =
        OverlayTemplate.LoadAll(Path.Combine(Path.GetTempPath(), "no-custom-templates"), NullLogger.Instance);

    private static MediaStream Audio(string codec, int ch, string? profile = null) =>
        new() { Type = MediaStreamType.Audio, Codec = codec, Channels = ch, Profile = profile };

    [Theory]
    [InlineData("/m/Blade Runner (1982)/Blade Runner (1982) {edition-Final Cut}.mkv", "FINAL CUT")]
    [InlineData("/m/The Dark Knight (2008)/The Dark Knight (2008) - Extended.mkv", "EXTENDED EDITION")]
    [InlineData("/m/Kingdom of Heaven (2005) - Director's Cut/movie.mkv", "DIRECTOR'S CUT")]
    [InlineData("/m/x/Aliens.1986.Directors.Cut.2160p.mkv", "DIRECTOR'S CUT")]
    [InlineData("/m/x/Seven Samurai (1954) Criterion.mkv", "CRITERION")]
    [InlineData("/m/The Final Cut (2004)/The Final Cut (2004).mkv", null)]
    [InlineData("/m/Extended Family (2010)/Extended Family (2010).mkv", null)]
    [InlineData("/m/Heat (1995)/Heat (1995).mkv", null)]
    public void Editions(string path, string? expected) => Assert.Equal(expected, BadgeInfo.Edition(path));

    [Fact]
    public void AudioLabels()
    {
        Assert.Equal(("DOLBY", "TrueHD 7.1"), BadgeInfo.Audio([Audio("truehd", 8)]));
        Assert.Equal(("DOLBY", "Digital+ 5.1"), BadgeInfo.Audio([Audio("eac3", 6)]));
        Assert.Equal(("DTS", "5.1"), BadgeInfo.Audio([Audio("dts", 6)]));
        Assert.Equal(("AAC", "Stereo"), BadgeInfo.Audio([Audio("aac", 2)]));
        // best track wins: TrueHD over a default AC3 commentary-style track
        Assert.Equal(("DOLBY", "TrueHD 7.1"), BadgeInfo.Audio([Audio("ac3", 2), Audio("truehd", 8)]));
        Assert.Null(BadgeInfo.Audio([]));
    }

    [Fact]
    public void Ratings()
    {
        Assert.Equal("7.8", BadgeInfo.Imdb(7.8f));
        Assert.Null(BadgeInfo.Imdb(null));
        Assert.Equal(("85%", true), BadgeInfo.RottenTomatoes(85));
        Assert.Equal(("42%", false), BadgeInfo.RottenTomatoes(42));
        Assert.Null(BadgeInfo.RottenTomatoes(null));
    }

    [Fact]
    public void AudioLabelOnlyWhenNoAudioTemplate()
    {
        var cfg = new PluginConfiguration();
        var atmos = Templates.Where(t => t.Name == "Dolby Atmos").ToList();
        var streams = new List<MediaStream> { Audio("truehd", 6, "Dolby TrueHD + Dolby Atmos") };
        Assert.DoesNotContain(ExtraBadges.For("/m/x (2020)/x (2020).mkv", streams, null, null, atmos, cfg), b => b.Kind == ExtraBadgeKind.Audio);
        Assert.Contains(ExtraBadges.For("/m/x (2020)/x (2020).mkv", streams, null, null, [], cfg), b => b.Kind == ExtraBadgeKind.Audio);
    }

    [Fact]
    public void SettingsTurnBadgesOff()
    {
        var cfg = new PluginConfiguration { ShowAudioLabel = false, ShowEdition = false, ShowImdbRating = false, ShowRottenTomatoes = false };
        Assert.Empty(ExtraBadges.For("/m/x (2020) - Extended.mkv", [Audio("aac", 2)], 7f, 80f, [], cfg));
    }

    [Fact]
    public void RendersSamplePosters()
    {
        var cfg = new PluginConfiguration();
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "poster-overlay-samples")).FullName;
        var samples = new (string File, string[] Templates, string Path, MediaStream[] Audio, float? Imdb, float? Rt)[]
        {
            ("1_batman.jpg", ["BluRay", "Dolby Atmos"], "/m/The Batman (2022)/The Batman (2022).mkv", [Audio("truehd", 6, "Dolby TrueHD + Dolby Atmos")], 7.8f, 85f),
            ("2_dark_knight_rises.jpg", ["BluRay", "IMAX"], "/m/The Dark Knight Rises (2012) Imax/tdkr.mkv", [Audio("dts", 6, "DTS-HD MA")], 7.6f, 87f),
            ("3_aliens_dc.jpg", ["4K", "DoVi Top Left"], "/m/Aliens (1986)/Aliens (1986) {edition-Director's Cut}.mkv", [Audio("truehd", 8)], 8.4f, 97f),
            ("4_extended_rotten.jpg", ["BluRay"], "/m/Some Movie (2016)/Some Movie (2016) - Extended.mkv", [Audio("eac3", 6)], 5.9f, 42f),
        };
        foreach (var s in samples)
        {
            var hits = Templates.Where(t => s.Templates.Contains(t.Name)).ToList();
            var extras = ExtraBadges.For(s.Path, s.Audio, s.Imdb, s.Rt, hits, cfg);
            var jpeg = OverlayRenderer.Render(Poster(), hits, extras, 92, NullLogger.Instance);
            File.WriteAllBytes(Path.Combine(dir, s.File), jpeg);
        }
    }

    [Fact]
    public void BadgeResourcesAreEmbedded()
    {
        var names = typeof(ExtraBadges).Assembly.GetManifestResourceNames();
        foreach (var f in new[] { "imdb.svg", "rt_fresh.svg", "rt_rotten.svg", "DejaVuSans-Bold.ttf", "DejaVuSans.ttf" })
        {
            Assert.Contains("Jellyfin.Plugin.PosterOverlays.Badges." + f, names);
        }
    }

    private static byte[] Poster()
    {
        using var bmp = new SKBitmap(1000, 1500);
        using (var c = new SKCanvas(bmp))
        using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 1500), new[] { new SKColor(140, 30, 30), new SKColor(25, 20, 40) }, SKShaderTileMode.Clamp) })
        {
            c.DrawRect(0, 0, 1000, 1500, p);
        }

        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
    }
}
