using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

/// <summary>
/// Restarts Desktop Duplication when a fullscreen transition invalidates the
/// current duplication session. FFmpeg can remain alive without producing any
/// more frames after an exclusive-mode transition, so media timestamp progress
/// is monitored. A static image's small file size is not a stall signal.
/// </summary>
public sealed class ResilientDisplayCaptureProcess : IManagedCaptureProcess
{
    private readonly FfmpegService _ffmpeg;
    private readonly AppSettings _settings;
    private readonly string _outputPath;
    private readonly bool _segmented;
    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<VideoTimelinePart> _parts = new();
    private ManagedFfmpegProcess? _current;
    private string _currentOutputPath = string.Empty;
    private Task? _monitorTask;
    private string _environmentSignature = string.Empty;
    private string _candidateSignature = string.Empty;
    private DateTime _candidateSinceUtc;
    private DateTime _currentStartedUtc;
    private DateTime _lastOutputGrowthUtc;
    private DateTime _lastRestartAttemptUtc;
    private long _lastOutputBytes;
    private Exception? _lastFailure;
    private int _restartFailures;
    public event EventHandler<string>? CaptureFailed;
    private int _partNumber;
    private bool _stopped;
    private bool _disposed;

    private ResilientDisplayCaptureProcess(FfmpegService ffmpeg, AppSettings settings, string outputPath, bool segmented, int targetWidth, int targetHeight)
    {
        _ffmpeg = ffmpeg;
        _settings = settings.Clone();
        _outputPath = outputPath;
        _segmented = segmented;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
    }

    public long StartedTimestamp => _parts.FirstOrDefault()?.StartedTimestamp ?? _initialTimestamp;
    private long _initialTimestamp = CaptureTimeline.Now;
    internal int? CurrentProcessId => _current?.Process.Id;

    public static async Task<ResilientDisplayCaptureProcess> StartAsync(FfmpegService ffmpeg, AppSettings settings, string outputPath, bool segmented)
    {
        var selected = CaptureTargetResolver.Resolve(settings);
        if (selected.Count == 0) throw new InvalidOperationException("録画する画面が見つかりません。");
        var left = selected.Min(target => target.Bounds.Left);
        var top = selected.Min(target => target.Bounds.Top);
        var sourceWidth = selected.Max(target => target.Bounds.Right) - left;
        var sourceHeight = selected.Max(target => target.Bounds.Bottom) - top;
        var targetHeight = FfmpegService.TargetHeight(settings.Resolution) ?? MakeEven(sourceHeight);
        var targetWidth = MakeEven((int)Math.Round(sourceWidth * (targetHeight / (double)sourceHeight)));
        var capture = new ResilientDisplayCaptureProcess(ffmpeg, settings, outputPath, segmented, targetWidth, targetHeight);
        capture._environmentSignature = ReadEnvironmentSignature();
        capture._candidateSignature = capture._environmentSignature;
        capture._candidateSinceUtc = DateTime.UtcNow;
        await capture.StartCoreAsync();
        capture._monitorTask = Task.Run(capture.MonitorAsync);
        return capture;
    }

    private async Task StartCoreAsync()
    {
        var path = NextOutputPath();
        var process = await _ffmpeg.StartDisplayCaptureCoreAsync(_settings, path, _segmented, _targetWidth, _targetHeight);
        var started = DateTime.UtcNow;
        _current = process;
        _currentOutputPath = path;
        _currentStartedUtc = started;
        _lastOutputGrowthUtc = DateTime.UtcNow;
        _lastOutputBytes = MeasureOutputBytes(path);
        _lastFailure = null;
        _restartFailures = 0;
        if (_partNumber == 1) _initialTimestamp = process.StartedTimestamp;
        if (!_segmented) _parts.Add(new VideoTimelinePart(path, process.StartedTimestamp));
    }

    private string NextOutputPath()
    {
        var number = _partNumber++;
        if (!_segmented)
            return Path.Combine(Path.GetDirectoryName(_outputPath)!, $"{Path.GetFileNameWithoutExtension(_outputPath)}.displaypart{number:D4}.mkv");
        var marker = _outputPath.IndexOf('%');
        return marker >= 0 ? _outputPath.Insert(marker, $"{number:D4}_") : _outputPath + $".{number:D4}.mkv";
    }

    private async Task MonitorAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try { await Task.Delay(350, _cancellation.Token); }
            catch (OperationCanceledException) { break; }
            var now = DateTime.UtcNow;
            var signature = ReadEnvironmentSignature();
            if (!string.Equals(signature, _candidateSignature, StringComparison.Ordinal))
            {
                _candidateSignature = signature;
                _candidateSinceUtc = now;
            }

            var bytes = MeasureOutputBytes(_currentOutputPath);
            if (bytes > _lastOutputBytes)
            {
                _lastOutputBytes = bytes;
                _lastOutputGrowthUtc = now;
            }

            var environmentChanged = !string.Equals(_candidateSignature, _environmentSignature, StringComparison.Ordinal)
                && now - _candidateSinceUtc >= TimeSpan.FromMilliseconds(550);
            var outputStalled = _current is not null
                && now - _currentStartedUtc >= TimeSpan.FromSeconds(3)
                && CaptureTimeline.Seconds(_current.LastProgressTimestamp, CaptureTimeline.Now) >= 8;
            var processExited = _current?.Process.HasExited == true;
            var retryMissing = _current is null && now - _lastRestartAttemptUtc >= TimeSpan.FromSeconds(1);
            if (!environmentChanged && !outputStalled && !processExited && !retryMissing) continue;

            _lastRestartAttemptUtc = now;
            await _gate.WaitAsync();
            try
            {
                if (_stopped) break;
                try
                {
                    await StopCoreAsync();
                    _environmentSignature = _candidateSignature;
                    await StartCoreAsync();
                }
                catch (Exception ex)
                {
                    _lastFailure = ex;
                    _current = null;
                    if (++_restartFailures >= 5)
                    {
                        CaptureFailed?.Invoke(this, "画面キャプチャの再接続に失敗したため録画を停止します。回復用データは保持されます。");
                        break;
                    }
                }
            }
            finally { _gate.Release(); }
        }
    }

    private async Task StopCoreAsync()
    {
        var current = _current;
        _current = null;
        if (current is null) return;
        try { await current.StopAsync(); }
        finally { current.Dispose(); }
    }

    internal async Task RestartForTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_stopped) return;
            await StopCoreAsync();
            await StartCoreAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        var endedTimestamp = CaptureTimeline.Now;
        _cancellation.Cancel();
        if (_monitorTask is not null) { try { await _monitorTask; } catch (OperationCanceledException) { } }
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
        if (_segmented) return;

        var validParts = _parts.Where(part => File.Exists(part.Path) && new FileInfo(part.Path).Length > 1024).ToList();
        if (validParts.Count == 0) throw new InvalidOperationException("画面録画の映像を保存できませんでした。", _lastFailure);
        await VideoPartAssembler.AssembleAsync(_ffmpeg, validParts, endedTimestamp, _outputPath, _targetWidth, _targetHeight, _settings.Quality == VideoQuality.High ? 60 : 30);
        foreach (var part in _parts) RecordingFinalizer.TryDelete(part.Path);
    }

    private long MeasureOutputBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        try
        {
            if (!_segmented) return File.Exists(path) ? new FileInfo(path).Length : 0;
            var directory = Path.GetDirectoryName(path)!;
            var fileName = Path.GetFileName(path);
            var marker = fileName.IndexOf('%');
            var prefix = marker >= 0 ? fileName[..marker] : Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(path);
            return Directory.EnumerateFiles(directory, prefix + "*" + extension).Sum(file => new FileInfo(file).Length);
        }
        catch { return 0; }
    }

    private static string ReadEnvironmentSignature()
    {
        var screens = System.Windows.Forms.Screen.AllScreens
            .OrderBy(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(screen => $"{screen.DeviceName}:{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}");
        var displaySignature = string.Join("|", screens);
        var fullscreenSignature = "windowed";
        try
        {
            var handle = GetForegroundWindow();
            if (handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle) && GetWindowRect(handle, out var rect))
            {
                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                var screen = System.Windows.Forms.Screen.FromRectangle(bounds);
                if (FollowingWindowCaptureProcess.IsCompleteFullscreen(bounds, screen.Bounds))
                {
                    GetWindowThreadProcessId(handle, out var processId);
                    fullscreenSignature = $"fullscreen:{processId}:{screen.DeviceName}";
                }
            }
        }
        catch { }
        return displaySignature + "#" + fullscreenSignature;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_stopped) { try { Task.Run(StopAsync).GetAwaiter().GetResult(); } catch { } }
        _current?.Dispose();
        _current = null;
        _cancellation.Dispose();
        _gate.Dispose();
    }

    private static int MakeEven(int value) => Math.Max(16, value + Math.Abs(value % 2));


    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
