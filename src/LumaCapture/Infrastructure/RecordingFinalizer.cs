using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed class RecordingFinalizer
{
    private readonly FfmpegService _ffmpeg;
    public RecordingFinalizer(FfmpegService ffmpeg) => _ffmpeg = ffmpeg;

    public async Task<string> FinalizeAsync(string videoPath, IReadOnlyList<AudioTrackFile> audioTracks, AppSettings settings, string outputDirectory, string baseName)
    {
        Directory.CreateDirectory(outputDirectory);
        var separate = settings.AudioOutputMode != AudioOutputMode.EmbedInVideo;
        var destinationDirectory = Path.Combine(Path.GetDirectoryName(videoPath)!, "export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destinationDirectory);
        var videoOutput = UniquePath(Path.Combine(destinationDirectory, separate ? "Video.mp4" : baseName + ".mp4"));
        if (settings.AudioOutputMode == AudioOutputMode.EmbedInVideo && audioTracks.Count > 0)
            await MuxAsync(videoPath, audioTracks, videoOutput);
        else
            await CopyVideoAsync(videoPath, videoOutput);

        switch (settings.AudioOutputMode)
        {
            case AudioOutputMode.SplitMixed when audioTracks.Count > 0:
                var mixed = UniquePath(Path.Combine(destinationDirectory, "MixedAudio.wav"));
                await MixWavAsync(audioTracks, mixed);
                break;
            case AudioOutputMode.SplitPerApplication:
                foreach (var track in audioTracks)
                {
                    var destination = UniquePath(Path.Combine(destinationDirectory, $"{ProcessCatalog.SafeLabel(track.Label)}.wav"));
                    if (track.IsMicrophone && track.Channels > 1) await CenterMicrophoneWavAsync(track.Path, destination);
                    else File.Copy(track.Path, destination, true);
                }
                break;
        }
        // Publish only complete exports. Failed work remains beside the sources.
        if (separate)
        {
            var destination = UniqueDirectory(Path.Combine(outputDirectory, baseName));
            Directory.Move(destinationDirectory, destination);
            return Path.Combine(destination, "Video.mp4");
        }
        var finalPath = UniquePath(Path.Combine(outputDirectory, baseName + ".mp4"));
        File.Move(videoOutput, finalPath);
        // Diagnostic sidecars may remain; successful publication must not turn
        // into a recording failure merely because cleanup cannot remove them.
        try { Directory.Delete(destinationDirectory, true); } catch { }
        return finalPath;
    }

    public async Task ConcatSegmentsAsync(IReadOnlyList<string> segments, string output)
    {
        var listPath = output + ".txt";
        await File.WriteAllLinesAsync(listPath, segments.Select(path => $"file '{path.Replace("'", "'\\''")}'"));
        try { await _ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-f", "concat", "-safe", "0", "-i", listPath, "-c", "copy", output }); }
        finally { TryDelete(listPath); }
    }

    public Task TrimFromEndAsync(string input, int seconds, string output) =>
        _ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-sseof", $"-{seconds}", "-i", input, "-map", "0:v:0", "-c:v", "copy", "-an", output });

    private Task CopyVideoAsync(string input, string output) =>
        _ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-i", input, "-map", "0:v:0", "-c:v", "copy", "-an", "-movflags", "+faststart", output });

    private Task MuxAsync(string video, IReadOnlyList<AudioTrackFile> audio, string output)
    {
        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "warning", "-i", video };
        foreach (var track in audio) args.AddRange(new[] { "-i", track.Path });
        var filters = new List<string>();
        var inputs = PrepareAudioInputs(audio, 1, filters);
        if (audio.Count == 1)
        {
            filters.Add($"{inputs[0]}apad[a]");
            args.AddRange(new[] { "-filter_complex", string.Join(";", filters), "-map", "0:v:0", "-map", "[a]" });
        }
        else
        {
            filters.Add($"{string.Concat(inputs)}amix=inputs={audio.Count}:duration=longest:normalize=0,apad[a]");
            args.AddRange(new[] { "-filter_complex", string.Join(";", filters), "-map", "0:v:0", "-map", "[a]" });
        }
        args.AddRange(new[] { "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", output });
        return _ffmpeg.RunAsync(args);
    }

    private Task MixWavAsync(IReadOnlyList<AudioTrackFile> audio, string output)
    {
        if (audio.Count == 1)
        {
            if (audio[0].IsMicrophone && audio[0].Channels > 1) return CenterMicrophoneWavAsync(audio[0].Path, output);
            File.Copy(audio[0].Path, output, true); return Task.CompletedTask;
        }
        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "warning" };
        foreach (var track in audio) args.AddRange(new[] { "-i", track.Path });
        var filters = new List<string>();
        var inputs = PrepareAudioInputs(audio, 0, filters);
        filters.Add($"{string.Concat(inputs)}amix=inputs={audio.Count}:duration=longest:normalize=0[a]");
        args.AddRange(new[] { "-filter_complex", string.Join(";", filters), "-map", "[a]", "-c:a", "pcm_s16le", "-rf64", "auto", output });
        return _ffmpeg.RunAsync(args);
    }

    private static List<string> PrepareAudioInputs(IReadOnlyList<AudioTrackFile> audio, int inputOffset, List<string> filters)
    {
        var inputs = new List<string>(audio.Count);
        for (var index = 0; index < audio.Count; index++)
        {
            var input = $"[{index + inputOffset}:a]";
            if (audio[index].IsMicrophone && audio[index].Channels > 1)
            {
                var label = $"mic{index}";
                filters.Add($"{input}pan=mono|c0=FL+FR,alimiter=limit=0.95:level=false[{label}]");
                input = $"[{label}]";
            }
            inputs.Add(input);
        }
        return inputs;
    }

    private Task CenterMicrophoneWavAsync(string input, string output) =>
        _ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-i", input, "-af", "pan=mono|c0=FL+FR,alimiter=limit=0.95:level=false", "-c:a", "pcm_s16le", "-rf64", "auto", output });

    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{name}_{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }

    public static string UniqueDirectory(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return path;
        for (var i = 2; i < 10000; i++)
        {
            var candidate = $"{path}_{i}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        return $"{path}_{Guid.NewGuid():N}";
    }

    public static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
