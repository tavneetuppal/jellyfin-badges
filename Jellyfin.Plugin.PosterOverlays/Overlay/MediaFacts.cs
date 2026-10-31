using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// Turns a library item into the fields Agregarr conditions test, and evaluates template conditions.
/// </summary>
public static class MediaFacts
{
    // Agregarr (Plex) codec names -> Jellyfin codec names
    private static readonly Dictionary<string, string> CodecAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dca"] = "dts",
    };

    /// <summary>
    /// Builds the fact table for an item.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <returns>Field name to value.</returns>
    public static Dictionary<string, object> For(BaseItem item)
    {
        var streams = item.GetMediaStreams();
        var video = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        var audios = streams.Where(s => s.Type == MediaStreamType.Audio).ToList();
        var audio = audios.FirstOrDefault(s => s.IsDefault) ?? audios.FirstOrDefault();

        var width = video?.Width ?? 0;
        var res = width >= 3200 ? "4k" : width >= 1800 ? "1080" : width >= 1200 ? "720" : "sd";
        var range = video?.VideoRangeType ?? VideoRangeType.Unknown;
        var dv = range.ToString().StartsWith("DOVI", StringComparison.Ordinal) && range != VideoRangeType.DOVIInvalid;

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["resolution"] = res,
            ["dolbyVision"] = dv,
            ["hdr"] = video?.VideoRange == VideoRange.HDR,
            ["hdr10Plus"] = range is VideoRangeType.HDR10Plus or VideoRangeType.DOVIWithHDR10Plus or VideoRangeType.DOVIWithELHDR10Plus,
            ["videoCodec"] = video?.Codec ?? string.Empty,
            ["audioCodec"] = audio?.Codec ?? string.Empty,
            ["audioChannels"] = audio?.Channels ?? 0,
            ["filePath"] = item.Path ?? string.Empty,
            ["title"] = item.Name ?? string.Empty,
            ["year"] = item.ProductionYear ?? 0,
        };
    }

    /// <summary>
    /// Checks a template's applicationCondition: rules in a section are AND-ed, sections are OR-ed.
    /// </summary>
    /// <param name="template">Template.</param>
    /// <param name="facts">Item facts.</param>
    /// <param name="logger">Logger for unsupported rules.</param>
    /// <returns>True when the template applies.</returns>
    public static bool Matches(OverlayTemplate template, Dictionary<string, object> facts, ILogger logger)
    {
        if (!template.Root.TryGetProperty("applicationCondition", out var cond)
            || cond.ValueKind != JsonValueKind.Object
            || !cond.TryGetProperty("sections", out var sections)
            || sections.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var section in sections.EnumerateArray())
        {
            if (!section.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (rules.EnumerateArray().All(r => Check(template.Name, r, facts, logger)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Check(string templateName, JsonElement rule, Dictionary<string, object> facts, ILogger logger)
    {
        var field = rule.GetProperty("field").GetString() ?? string.Empty;
        var op = rule.GetProperty("operator").GetString() ?? string.Empty;
        if (!facts.TryGetValue(field, out var haveObj))
        {
            logger.LogWarning("Template {Template}: condition field '{Field}' is not supported; treated as no match", templateName, field);
            return false;
        }

        var have = Norm(haveObj);
        var value = rule.GetProperty("value");
        var wants = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(v => Norm(v)).ToList()
            : [Norm(value)];
        var want = wants.FirstOrDefault() ?? string.Empty;

        switch (op)
        {
            case "eq": return have == want;
            case "ne" or "neq": return have != want;
            case "in": return wants.Contains(have);
            case "notIn" or "nin": return !wants.Contains(have);
            case "contains": return have.Contains(want, StringComparison.Ordinal);
            case "notContains" or "ncontains": return !have.Contains(want, StringComparison.Ordinal);
            case "gt" or "gte" or "lt" or "lte":
                if (!double.TryParse(have, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                    || !double.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                {
                    return false;
                }

                return op switch { "gt" => a > b, "gte" => a >= b, "lt" => a < b, _ => a <= b };
            default:
                logger.LogWarning("Template {Template}: operator '{Op}' is not supported; treated as no match", templateName, op);
                return false;
        }
    }

    private static string Norm(object v)
    {
        var s = v switch
        {
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? string.Empty,
            JsonElement { ValueKind: JsonValueKind.True } => "true",
            JsonElement { ValueKind: JsonValueKind.False } => "false",
            JsonElement e => e.GetRawText(),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? string.Empty,
        };
        s = s.ToLowerInvariant();
        return CodecAliases.TryGetValue(s, out var alias) ? alias : s;
    }
}
