using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.PosterOverlays.Overlay;

/// <summary>
/// Tags posters written by the plugin with a JPEG comment naming the clean backup they were drawn from.
/// This lets the plugin recognise its own output when Jellyfin hands an overlaid poster to a different item
/// (e.g. a file upgrade with a new name re-using poster.jpg from the media folder), so badges never stack.
/// </summary>
public static class JpegMarker
{
    private const string Prefix = "PosterOverlays:";

    /// <summary>
    /// Inserts a comment segment right after the JPEG start-of-image marker.
    /// </summary>
    /// <param name="jpeg">Encoded JPEG.</param>
    /// <param name="original">Backup file name of the clean original.</param>
    /// <returns>JPEG with the marker.</returns>
    public static byte[] Add(byte[] jpeg, string original)
    {
        var payload = Encoding.ASCII.GetBytes(Prefix + original);
        var len = payload.Length + 2;
        var output = new byte[jpeg.Length + 2 + len];
        output[0] = 0xFF;
        output[1] = 0xD8;
        output[2] = 0xFF;
        output[3] = 0xFE;
        output[4] = (byte)(len >> 8);
        output[5] = (byte)(len & 0xFF);
        payload.CopyTo(output, 6);
        Array.Copy(jpeg, 2, output, 6 + payload.Length, jpeg.Length - 2);
        return output;
    }

    /// <summary>
    /// Reads the backup file name from a poster written by the plugin.
    /// </summary>
    /// <param name="path">Poster path.</param>
    /// <returns>Backup file name, or null for any other image.</returns>
    public static string? Read(string path)
    {
        var head = new byte[64 * 1024];
        int n;
        using (var fs = File.OpenRead(path))
        {
            n = fs.ReadAtLeast(head, head.Length, false);
        }

        return Read(head.AsSpan(0, n));
    }

    /// <summary>
    /// Reads the backup file name from the start of an encoded image.
    /// </summary>
    /// <param name="data">Image bytes (at least the header segments).</param>
    /// <returns>Backup file name, or null.</returns>
    public static string? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return null;
        }

        // Walk header segments until start-of-scan.
        var i = 2;
        while (i + 4 <= data.Length && data[i] == 0xFF)
        {
            var type = data[i + 1];
            if (type == 0xDA)
            {
                break;
            }

            var len = (data[i + 2] << 8) | data[i + 3];
            if (len < 2 || i + 2 + len > data.Length)
            {
                break;
            }

            if (type == 0xFE)
            {
                var text = Encoding.ASCII.GetString(data.Slice(i + 4, len - 2));
                if (text.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    var name = Path.GetFileName(text[Prefix.Length..]);
                    return name.Length > 0 ? name : null;
                }
            }

            i += 2 + len;
        }

        return null;
    }
}
