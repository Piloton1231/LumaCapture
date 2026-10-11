using System.Globalization;

namespace LumaCapture.Infrastructure;

public sealed record VideoTimelinePart(string Path, long StartedTimestamp);

public static class VideoPartAssembler
{
    public static async Task AssembleAsync(FfmpegService ffmpeg, IReadOnlyList<VideoTimelinePart> parts, long endedTimestamp, string output, int width, int height, int fps)
    {
        if (parts.Count == 0) throw new InvalidOperationException("保存できる映像がありません。回復用データは保持されます。");
        if (parts.Count == 1)
        {
            await ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-i", parts[0].Path, "-map", "0:v:0", "-c:v", "copy", "-an", "-f", "matroska", output });
            return;
        }
        var normalized = new List<string>();
        // One decoder at a time bounds memory even after hundreds of reconnects.
        // Each part occupies its actual monotonic interval, including suspension.
        for (var index = 0; index < parts.Count; index++)
        {
            var end = index + 1 < parts.Count ? parts[index + 1].StartedTimestamp : endedTimestamp;
            var duration = Math.Max(1d / fps, CaptureTimeline.Seconds(parts[index].StartedTimestamp, end)).ToString("0.000000", CultureInfo.InvariantCulture);
            var path = output + $".normalized{index:D5}.mkv";
            var filter = $"scale={width}:{height}:force_original_aspect_ratio=decrease:flags=bilinear,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,setpts=PTS-STARTPTS,fps={fps},tpad=stop_mode=clone:stop=-1,trim=duration={duration}";
            await ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-i", parts[index].Path, "-map", "0:v:0", "-vf", filter, "-an", "-c:v", "libx264", "-threads", "4", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-f", "matroska", path });
            normalized.Add(path);
        }
        await new RecordingFinalizer(ffmpeg).ConcatSegmentsAsync(normalized, output);
        foreach (var path in normalized) RecordingFinalizer.TryDelete(path);
    }
}
