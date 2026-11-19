using System.Globalization;
using System.Text.Json;

namespace LingoCue.Media.Probing;

/// <summary>Parses <c>ffprobe -print_format json -show_format -show_streams</c> output.</summary>
internal static class FfprobeJsonParser
{
    public static MediaInfo Parse(string filePath, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var format = root.TryGetProperty("format", out var f) ? f : default;
        var formatName = GetString(format, "format_name") ?? "unknown";
        var duration = ParseSeconds(GetString(format, "duration"));
        var size = long.TryParse(GetString(format, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : (long?)null;

        VideoStreamInfo? video = null;
        var audio = new List<AudioStreamInfo>();

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                switch (GetString(stream, "codec_type"))
                {
                    case "video" when video is null && !IsAttachedPicture(stream):
                        video = ParseVideo(stream);
                        if (duration == TimeSpan.Zero)
                        {
                            duration = ParseSeconds(GetString(stream, "duration"));
                        }

                        break;

                    case "audio":
                        audio.Add(ParseAudio(stream));
                        break;
                }
            }
        }

        return new MediaInfo(filePath, formatName, duration, size, video, audio);
    }

    private static VideoStreamInfo ParseVideo(JsonElement stream)
    {
        var rate = ParseRational(GetString(stream, "avg_frame_rate"));
        if (rate <= 0)
        {
            rate = ParseRational(GetString(stream, "r_frame_rate"));
        }

        return new VideoStreamInfo(
            Index: GetInt(stream, "index"),
            Codec: GetString(stream, "codec_name") ?? "unknown",
            Width: GetInt(stream, "width"),
            Height: GetInt(stream, "height"),
            FrameRate: rate,
            Rotation: ParseRotation(stream),
            PixelFormat: GetString(stream, "pix_fmt"),
            ColorTransfer: GetString(stream, "color_transfer"));
    }

    private static AudioStreamInfo ParseAudio(JsonElement stream)
    {
        var language = stream.TryGetProperty("tags", out var tags) ? GetString(tags, "language") : null;
        return new AudioStreamInfo(
            Index: GetInt(stream, "index"),
            Codec: GetString(stream, "codec_name") ?? "unknown",
            Channels: GetInt(stream, "channels"),
            SampleRate: int.TryParse(GetString(stream, "sample_rate"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sr) ? sr : 0,
            Language: language);
    }

    // Rotation lives in the display matrix side data (modern) or the "rotate" tag (older files).
    private static int ParseRotation(JsonElement stream)
    {
        double? degrees = null;

        if (stream.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number)
                {
                    degrees = r.GetDouble();
                    break;
                }
            }
        }

        if (degrees is null && stream.TryGetProperty("tags", out var tags) &&
            double.TryParse(GetString(tags, "rotate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var tagged))
        {
            degrees = tagged;
        }

        if (degrees is null)
        {
            return 0;
        }

        // Normalize to 0/90/180/270; display-matrix rotation is counter-clockwise and may be negative.
        var normalized = (int)Math.Round(degrees.Value / 90d) * 90 % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static bool IsAttachedPicture(JsonElement stream)
        => stream.TryGetProperty("disposition", out var d) &&
           d.TryGetProperty("attached_pic", out var a) &&
           a.ValueKind == JsonValueKind.Number && a.GetInt32() == 1;

    private static double ParseRational(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            return num / den;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) ? plain : 0;
    }

    private static TimeSpan ParseSeconds(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static int GetInt(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var result)
            ? result
            : 0;
}
