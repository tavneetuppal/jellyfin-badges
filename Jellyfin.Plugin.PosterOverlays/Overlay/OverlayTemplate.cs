using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// An Agregarr overlay template export (template.json + assets/icons/*.svg in a zip).
/// </summary>
public sealed class OverlayTemplate
{
    private OverlayTemplate(string name, string source, string hash, JsonElement root, Dictionary<string, byte[]> icons)
    {
        Name = name;
        Source = source;
        Hash = hash;
        Root = root;
        Icons = icons;
    }

    /// <summary>Gets the template name.</summary>
    public string Name { get; }

    /// <summary>Gets where the template came from ("built-in" or "custom").</summary>
    public string Source { get; }

    /// <summary>Gets a short hash of the template contents, used to re-render when a template changes.</summary>
    public string Hash { get; }

    /// <summary>Gets the parsed template.json.</summary>
    public JsonElement Root { get; }

    /// <summary>Gets the SVG/PNG icons keyed by file name.</summary>
    public Dictionary<string, byte[]> Icons { get; }

    /// <summary>
    /// Loads built-in templates, then custom templates from the templates folder (a custom template replaces a built-in one with the same name).
    /// </summary>
    /// <param name="customFolder">Folder with user template zips.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>Templates ordered by name.</returns>
    public static List<OverlayTemplate> LoadAll(string customFolder, ILogger logger)
    {
        var byName = new Dictionary<string, OverlayTemplate>(StringComparer.OrdinalIgnoreCase);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var res in asm.GetManifestResourceNames().Where(r => r.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            using var s = asm.GetManifestResourceStream(res)!;
            TryAdd(byName, ReadAll(s), "built-in", res, logger);
        }

        if (Directory.Exists(customFolder))
        {
            foreach (var file in Directory.EnumerateFiles(customFolder, "*.zip"))
            {
                TryAdd(byName, File.ReadAllBytes(file), "custom", file, logger);
            }
        }

        return byName.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void TryAdd(Dictionary<string, OverlayTemplate> byName, byte[] zipBytes, string source, string origin, ILogger logger)
    {
        try
        {
            var t = Parse(zipBytes, source);
            byName[t.Name] = t;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Skipping invalid overlay template {Origin}", origin);
        }
    }

    private static OverlayTemplate Parse(byte[] zipBytes, string source)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("template.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("template.json missing");
        using var doc = JsonDocument.Parse(ReadAll(entry.Open()));
        var root = doc.RootElement.Clone();

        var icons = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries.Where(e => e.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
        {
            icons[e.Name] = ReadAll(e.Open());
        }

        var name = root.GetProperty("name").GetString() ?? throw new InvalidDataException("template has no name");
        var hash = Convert.ToHexString(SHA256.HashData(zipBytes))[..12];
        return new OverlayTemplate(name, source, hash, root, icons);
    }

    private static byte[] ReadAll(Stream s)
    {
        using (s)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
