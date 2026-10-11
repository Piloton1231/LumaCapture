using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.Win32.SafeHandles;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed class FfmpegService
{
    public string ExecutablePath { get; }

    public FfmpegService()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "ffmpeg.exe"),
            @"C:\path\ffmpeg.exe"
        };
        ExecutablePath = candidates.FirstOrDefault(File.Exists) ?? "ffmpeg.exe";
    }

    public async Task<IManagedCaptureProcess> StartScreenCaptureAsync(AppSettings settings, string outputPath, bool segmented)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (settings.CaptureArea == CaptureArea.SpecificApplication)
            return await FollowingWindowCaptureProcess.StartAsync(this, settings, outputPath, segmented);
        return await ResilientDisplayCaptureProcess.StartAsync(this, settings, outputPath, segmented);
    }

    internal async Task<ManagedFfmpegProcess> StartDisplayCaptureCoreAsync(AppSettings settings, string outputPath, bool segmented, int? fixedWidth = null, int? fixedHeight = null)
    {
        var errors = new List<string>();

        try { return await StartVerifiedAsync(BuildDesktopDuplicationArguments(settings, outputPath, segmented, true, fixedWidth, fixedHeight)); }
        catch (Exception ex) { errors.Add("高速録画: " + ex.Message); }

        try { return await StartVerifiedAsync(BuildDesktopDuplicationArguments(settings, outputPath, segmented, false, fixedWidth, fixedHeight)); }
        catch (Exception ex) { errors.Add("Windows録画: " + ex.Message); }

        // Do not silently switch to GDI: exclusive games can then become a
        // convincing recording of the desktop instead of the requested content.
        throw new InvalidOperationException("録画エンジンを開始できませんでした。" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    internal async Task<IManagedCaptureProcess> StartFullscreenDisplayPartAsync(AppSettings settings, string displayDeviceName, string outputPath, int targetWidth, int targetHeight)
    {
        var displaySettings = settings.Clone();
        displaySettings.CaptureArea = CaptureArea.SpecificDisplay;
        displaySettings.CaptureDisplayDeviceName = displayDeviceName;
        displaySettings.Resolution = VideoResolution.Original;
        return await StartDisplayCaptureCoreAsync(displaySettings, outputPath, false, targetWidth, targetHeight);
    }

    private static List<string> BuildDesktopDuplicationArguments(AppSettings settings, string outputPath, bool segmented, bool useNvenc, int? fixedWidth = null, int? fixedHeight = null)
    {
        var profile = QualityProfile.From(settings.Quality);
        var framesPerSecond = segmented ? Math.Min(profile.FramesPerSecond, 30) : profile.FramesPerSecond;
        var crf = segmented ? Math.Min(profile.Crf + 2, 30) : profile.Crf;
        var drawMouse = !segmented && settings.CaptureCursor;
        var selected = CaptureTargetResolver.Resolve(settings).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("録画する画面が見つかりません。");

        var minimumX = selected.Min(item => item.Bounds.X);
        var minimumY = selected.Min(item => item.Bounds.Y);
        var sourceHeight = selected.Max(item => item.Bounds.Bottom) - minimumY;
        var targetHeight = TargetHeight(settings.Resolution);
        var sourceWidth = selected.Max(item => item.Bounds.Right) - minimumX;
        var requiresResize = fixedWidth.HasValue && fixedHeight.HasValue
            ? fixedWidth.Value != sourceWidth || fixedHeight.Value != sourceHeight
            : targetHeight.HasValue && targetHeight.Value != sourceHeight;
        var directHardwarePath = useNvenc && selected.Length == 1 && !requiresResize && selected[0].CropBounds is null;
        var softwareProcessing = fixedWidth.HasValue && fixedHeight.HasValue
            ? $"scale={fixedWidth}:{fixedHeight}:force_original_aspect_ratio=decrease:flags=lanczos,pad={fixedWidth}:{fixedHeight}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,format=yuv420p,setpts=PTS-STARTPTS"
            : (requiresResize ? $"scale=-2:{targetHeight}:flags=lanczos," : string.Empty) + "pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p,setpts=PTS-STARTPTS";
        var graph = new List<string>();
        for (var position = 0; position < selected.Length; position++)
        {
            var item = selected[position];
            if (directHardwarePath)
                graph.Add($"ddagrab=output_idx={item.ScreenIndex}:draw_mouse={(drawMouse ? 1 : 0)}:framerate={framesPerSecond},setpts=PTS-STARTPTS[out]");
            else
            {
                var crop = item.CropBounds is { } bounds
                    ? $",crop={bounds.Width}:{bounds.Height}:{bounds.X - item.Screen.Bounds.X}:{bounds.Y - item.Screen.Bounds.Y}"
                    : string.Empty;
                graph.Add($"ddagrab=output_idx={item.ScreenIndex}:draw_mouse={(drawMouse ? 1 : 0)}:framerate={framesPerSecond},hwdownload,format=bgra{crop}[v{position}]");
            }
        }

        if (!directHardwarePath && selected.Length == 1)
            graph.Add($"[v0]{softwareProcessing}[out]");
        else if (!directHardwarePath)
        {
            var inputs = string.Concat(Enumerable.Range(0, selected.Length).Select(index => $"[v{index}]"));
            var layout = string.Join("|", selected.Select(item => $"{item.Bounds.X - minimumX}_{item.Bounds.Y - minimumY}"));
            graph.Add($"{inputs}xstack=inputs={selected.Length}:layout={layout}:fill=black,{softwareProcessing}[out]");
        }

        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "warning", "-filter_complex", string.Join(";", graph), "-map", "[out]", "-an" };
        if (useNvenc)
        {
            var nvencPreset = segmented ? "p2" : profile.NvencPreset;
            args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", nvencPreset, "-tune", "ll", "-rc", "vbr", "-cq", crf.ToString(), "-b:v", "0" });
            if (!directHardwarePath) args.AddRange(new[] { "-pix_fmt", "yuv420p" });
            args.AddRange(new[] { "-g", (framesPerSecond * 2).ToString(), "-keyint_min", (framesPerSecond * 2).ToString() });
        }
        else
        {
            var preset = segmented ? "ultrafast" : profile.Preset;
            args.AddRange(new[] { "-c:v", "libx264", "-preset", preset, "-crf", crf.ToString(), "-pix_fmt", "yuv420p", "-g", (framesPerSecond * 2).ToString(), "-keyint_min", (framesPerSecond * 2).ToString(), "-sc_threshold", "0" });
        }
        AddOutputArguments(args, outputPath, segmented);
        return args;
    }

    internal static int? TargetHeight(VideoResolution resolution) => resolution switch
    {
        VideoResolution.P2160 => 2160,
        VideoResolution.P1440 => 1440,
        VideoResolution.P1080 => 1080,
        VideoResolution.P720 => 720,
        _ => null
    };

    internal ManagedFfmpegProcess StartRawVideoEncoder(AppSettings settings, string output, int width, int height, bool hardware)
    {
        var profile = QualityProfile.From(settings.Quality);
        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "warning", "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{width}x{height}", "-framerate", profile.FramesPerSecond.ToString(), "-i", "pipe:0", "-an" };
        args.AddRange(hardware
            ? new[] { "-c:v", "h264_nvenc", "-preset", profile.NvencPreset, "-tune", "ll", "-rc", "vbr", "-cq", profile.Crf.ToString(), "-b:v", "0", "-bf", "0", "-rc-lookahead", "0" }
            : new[] { "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-crf", profile.Crf.ToString() });
        args.AddRange(new[] { "-pix_fmt", "yuv420p", "-g", (profile.FramesPerSecond * 2).ToString() });
        AddOutputArguments(args, output, false);
        var process = Start(args);
        process.BinaryInput = true;
        return process;
    }

    private static void AddOutputArguments(List<string> args, string outputPath, bool segmented)
    {
        if (segmented)
        {
            args.AddRange(new[] { "-force_key_frames", "expr:gte(t,n_forced*2)", "-f", "segment", "-segment_time", "2", "-segment_time_delta", "0.05", "-reset_timestamps", "1", "-segment_format", "matroska", outputPath });
        }
        else args.AddRange(new[] { "-flush_packets", "1", "-cluster_time_limit", "0", "-f", "matroska", outputPath });
    }

    private async Task<ManagedFfmpegProcess> StartVerifiedAsync(IEnumerable<string> arguments)
    {
        var managed = Start(arguments);
        var deadline = CaptureTimeline.Now;
        while (!managed.Process.HasExited && managed.MediaSeconds <= 0 && CaptureTimeline.Seconds(deadline, CaptureTimeline.Now) < 6)
            await Task.Delay(100);
        if (managed.Process.HasExited || managed.MediaSeconds <= 0)
        {
            await managed.StopAsync();
            var error = await managed.ReadErrorAsync();
            managed.Dispose();
            throw new InvalidOperationException("録画エンジンから映像を受信できませんでした。" + Environment.NewLine + error);
        }
        return managed;
    }

    public async Task RunAsync(IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        using var managed = Start(arguments);
        // Long recordings may legitimately take hours to normalize. Time out a
        // stalled encoder, not a healthy job that has crossed a fixed duration.
        var idleLimit = timeout ?? TimeSpan.FromMinutes(2);
        while (!managed.Process.HasExited)
        {
            await Task.Delay(500).ConfigureAwait(false);
            if (CaptureTimeline.Seconds(managed.LastProgressTimestamp, CaptureTimeline.Now) <= idleLimit.TotalSeconds) continue;
            try { managed.Process.Kill(true); } catch { }
            await managed.Process.WaitForExitAsync().ConfigureAwait(false);
            throw new TimeoutException("動画処理の進行が停止しました。回復用ファイルは保持されます。" + Environment.NewLine + await managed.ReadErrorAsync());
        }
        if (managed.Process.ExitCode != 0)
            throw new InvalidOperationException((await managed.ReadErrorAsync()).Trim());
    }

    private ManagedFfmpegProcess Start(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        info.ArgumentList.Add("-progress"); info.ArgumentList.Add("pipe:1");
        info.ArgumentList.Add("-stats_period"); info.ArgumentList.Add("1");
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var binary = info.ArgumentList.Contains("pipe:0");
        var process = CaptureShutdown.Start(info, child =>
        {
            if (binary) child.StandardInput.Close();
            else { child.StandardInput.WriteLine("q"); child.StandardInput.Flush(); }
        });
        var lastArgument = info.ArgumentList.LastOrDefault();
        return new ManagedFfmpegProcess(process, FfmpegChildProcessJob.TryAttach(process), lastArgument is not null && Path.IsPathRooted(lastArgument) ? lastArgument + ".ffmpeg.log" : null);
    }

    public static bool ExtractEmbeddedExecutable(string destination)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("LumaCapture.Tools.ffmpeg.exe");
        if (source is null) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
        return true;
    }

    private sealed record QualityProfile(int FramesPerSecond, int Crf, string Preset, string NvencPreset)
    {
        public static QualityProfile From(VideoQuality quality) => quality switch
        {
            VideoQuality.High => new QualityProfile(60, 18, "veryfast", "p5"),
            VideoQuality.Compact => new QualityProfile(30, 28, "veryfast", "p3"),
            _ => new QualityProfile(30, 22, "veryfast", "p4")
        };
    }
}

public interface IManagedCaptureProcess : IDisposable
{
    long StartedTimestamp { get; }
    Task StopAsync();
}

public sealed class ManagedFfmpegProcess : IManagedCaptureProcess
{
    internal bool BinaryInput { get; set; }
    public Process Process { get; }
    private readonly Task _error;
    private readonly Task _progress;
    private readonly System.Text.StringBuilder _errorTail = new();
    public long StartedTimestamp { get; } = CaptureTimeline.Now;
    private long _lastProgressTimestamp = CaptureTimeline.Now;
    private long _mediaMicroseconds = -1;
    public long LastProgressTimestamp => Interlocked.Read(ref _lastProgressTimestamp);
    public double MediaSeconds => Math.Max(0, Interlocked.Read(ref _mediaMicroseconds)) / 1_000_000d;
    private readonly SafeFileHandle? _job;
    private readonly string? _logPath;
    internal ManagedFfmpegProcess(Process process, SafeFileHandle? job, string? logPath = null)
    {
        Process = process;
        _job = job;
        _logPath = logPath;
        _error = DrainErrorsAsync();
        _progress = DrainProgressAsync();
    }

    private async Task DrainErrorsAsync()
    {
        while (await Process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (_errorTail)
            {
                _errorTail.AppendLine(line);
                if (_errorTail.Length > 32768) _errorTail.Remove(0, _errorTail.Length - 32768);
            }
        }
    }

    private async Task DrainProgressAsync()
    {
        while (await Process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), out var time) && time > Interlocked.Read(ref _mediaMicroseconds))
            {
                Interlocked.Exchange(ref _mediaMicroseconds, time);
                Interlocked.Exchange(ref _lastProgressTimestamp, CaptureTimeline.Now);
            }
        }
    }

    public async Task StopAsync()
    {
        if (Process.HasExited) { await Task.WhenAll(_error, _progress); return; }
        try { if (BinaryInput) Process.StandardInput.Close(); else { await Process.StandardInput.WriteLineAsync("q"); await Process.StandardInput.FlushAsync(); } } catch { }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try { await Process.WaitForExitAsync(cts.Token); }
        catch
        {
            try { Process.Kill(true); } catch { }
            await Process.WaitForExitAsync();
        }
        await Task.WhenAll(_error, _progress);
    }

    public async Task<string> ReadErrorAsync() { await _error.ConfigureAwait(false); lock (_errorTail) return _errorTail.ToString(); }
    public void Dispose()
    {
        try { if (_logPath is not null) { lock (_errorTail) { if (_errorTail.Length > 0) File.WriteAllText(_logPath, _errorTail.ToString()); } } } catch { }
        CaptureShutdown.Forget(Process);
        Process.Dispose(); _job?.Dispose();
    }
}

internal static class FfmpegChildProcessJob
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int ExtendedLimitInformationClass = 9;

    public static SafeFileHandle? TryAttach(Process process)
    {
        SafeFileHandle? job = null;
        try
        {
            var raw = CreateJobObject(IntPtr.Zero, null);
            if (raw == IntPtr.Zero || raw == new IntPtr(-1)) return null;
            job = new SafeFileHandle(raw, true);
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose }
            };
            if (!SetInformationJobObject(job, ExtendedLimitInformationClass, ref information, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>())
                || !AssignProcessToJobObject(job, process.Handle))
            {
                job.Dispose();
                return null;
            }
            return job;
        }
        catch { job?.Dispose(); return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref JobObjectExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
