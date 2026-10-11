using System.Drawing;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

/// <summary>
/// Captures an isolated window while it is windowed. A foreground fullscreen
/// game is captured through DXGI Desktop Duplication because Windows Graphics
/// Capture can return the desktop or a stale frame for independent/exclusive
/// fullscreen Direct3D swap chains.
/// </summary>
public sealed class FollowingWindowCaptureProcess : IManagedCaptureProcess
{
    private enum CaptureSourceMode { Window, FullscreenDesktopDuplication, Suspended }

    private readonly FfmpegService _ffmpeg;
    private readonly AppSettings _settings;
    private readonly string _outputPath;
    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly int _targetProcessId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly List<VideoTimelinePart> _parts = new();
    private ClockedWindowCaptureProcess? _windowFfmpegCapture;
    private IManagedCaptureProcess? _fullscreenCapture;
    private Task? _monitorTask;
    private Rectangle _observedBounds;
    private Rectangle _warningStartBounds;
    private DateTime _lastSizeChangeUtc;
    private IntPtr _targetHandle;
    private string _displayDeviceName = string.Empty;
    private CaptureSourceMode _mode = CaptureSourceMode.Suspended;
    private bool _warningPending;
    private bool _stopped;
    private bool _disposed;
    private bool _targetEndedRaised;
    private Rectangle _partBounds;
    private int _restartFailures;
    private int _partNumber;
    private int _missingWindowChecks;
    private int _fullscreenBackgroundChecks;
    public long StartedTimestamp => _parts.FirstOrDefault()?.StartedTimestamp ?? _initialTimestamp;
    private long _initialTimestamp = CaptureTimeline.Now;
    public event EventHandler<string>? Warning;
    public event EventHandler<string>? TargetEnded;

    private FollowingWindowCaptureProcess(FfmpegService ffmpeg, AppSettings settings, string outputPath, ResolvedApplicationWindow target, int targetWidth, int targetHeight)
    {
        _ffmpeg = ffmpeg;
        _settings = settings.Clone();
        _outputPath = outputPath;
        _targetProcessId = target.ProcessId;
        _observedBounds = target.Bounds;
        _warningStartBounds = target.Bounds;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
    }

    public static async Task<FollowingWindowCaptureProcess> StartAsync(FfmpegService ffmpeg, AppSettings settings, string outputPath, bool segmented)
    {
        if (segmented) throw new InvalidOperationException("アプリ録画は直前クリップの録画元には使用できません。");
        var target = CaptureTargetResolver.ResolveApplicationWindow(settings.CaptureApplicationProcessName);
        if (CaptureTargetResolver.TryResolveForegroundApplicationWindow(settings.CaptureApplicationProcessName, target.ProcessId, out var foreground) && foreground is not null)
        {
            var foregroundScreen = System.Windows.Forms.Screen.FromRectangle(foreground.Bounds);
            if (IsCompleteFullscreen(foreground.Bounds, foregroundScreen.Bounds)) target = foreground;
        }
        var targetHeight = FfmpegService.TargetHeight(settings.Resolution) ?? MakeEven(target.Bounds.Height);
        var targetWidth = MakeEven((int)Math.Round(target.Bounds.Width * (targetHeight / (double)target.Bounds.Height)));
        var capture = new FollowingWindowCaptureProcess(ffmpeg, settings, outputPath, target, targetWidth, targetHeight);
        var screen = System.Windows.Forms.Screen.FromRectangle(target.Bounds);
        var mode = IsCompleteFullscreen(target.Bounds, screen.Bounds) ? CaptureSourceMode.FullscreenDesktopDuplication : CaptureSourceMode.Window;
        await capture.StartPartAsync(target, mode);
        capture._monitorTask = Task.Run(capture.MonitorWindowAsync);
        return capture;
    }

    private async Task StartPartAsync(ResolvedApplicationWindow target, CaptureSourceMode mode)
    {
        if (mode == CaptureSourceMode.Suspended) { _mode = mode; return; }
        var screen = System.Windows.Forms.Screen.FromRectangle(target.Bounds);
        var stem = Path.Combine(Path.GetDirectoryName(_outputPath)!, $"{Path.GetFileNameWithoutExtension(_outputPath)}.applicationpart{_partNumber++:D4}");
        if (mode == CaptureSourceMode.FullscreenDesktopDuplication)
        {
            var path = stem + ".mkv";
            _fullscreenCapture = await _ffmpeg.StartFullscreenDisplayPartAsync(_settings, screen.DeviceName, path, _targetWidth, _targetHeight);
            _parts.Add(new VideoTimelinePart(path, _fullscreenCapture.StartedTimestamp));

        }
        else
        {
            var path = stem + ".mkv";
            _windowFfmpegCapture = await ClockedWindowCaptureProcess.StartAsync(_ffmpeg, _settings, target.Handle, path, _targetWidth, _targetHeight);
            _parts.Add(new VideoTimelinePart(path, _windowFfmpegCapture.StartedTimestamp));
        }
        _partBounds = target.Bounds;
        _restartFailures = 0;
        _mode = mode;
        _targetHandle = target.Handle;
        _displayDeviceName = screen.DeviceName;
    }

    private async Task MonitorWindowAsync()
    {
        while (!_monitorCancellation.IsCancellationRequested)
        {
            try { await Task.Delay(300, _monitorCancellation.Token); }
            catch (OperationCanceledException) { break; }

            var foregroundFullscreen = false;
            ResolvedApplicationWindow? target = null;
            if (CaptureTargetResolver.TryResolveForegroundApplicationWindow(_settings.CaptureApplicationProcessName, _targetProcessId, out var foreground) && foreground is not null)
            {
                var foregroundScreen = System.Windows.Forms.Screen.FromRectangle(foreground.Bounds);
                foregroundFullscreen = IsCompleteFullscreen(foreground.Bounds, foregroundScreen.Bounds);
                if (foregroundFullscreen) target = foreground;
            }
            if (target is null) CaptureTargetResolver.TryResolveApplicationWindow(_settings.CaptureApplicationProcessName, _targetProcessId, out target);

            if (target is null)
            {
                _missingWindowChecks++;
                if (_missingWindowChecks >= 2 && HasActiveSource) await SuspendCaptureAsync();
                if (_missingWindowChecks >= 3 && !CaptureTargetResolver.IsProcessRunning(_targetProcessId))
                {
                    await RaiseTargetEndedAsync();
                    break;
                }
                continue;
            }

            _missingWindowChecks = 0;
            var now = DateTime.UtcNow;
            if (SizeChanged(target.Bounds, _observedBounds))
            {
                if (!_warningPending) _warningStartBounds = _observedBounds;
                _observedBounds = target.Bounds;
                _lastSizeChangeUtc = now;
                _warningPending = true;
            }

            var screen = System.Windows.Forms.Screen.FromRectangle(target.Bounds);
            var fullscreen = foregroundFullscreen || IsCompleteFullscreen(target.Bounds, screen.Bounds);
            if (fullscreen && !foregroundFullscreen)
            {
                _fullscreenBackgroundChecks++;
                if (_fullscreenBackgroundChecks >= 1)
                {
                    await SuspendCaptureAsync();
                    continue;
                }
            }
            else _fullscreenBackgroundChecks = 0;

            var desiredMode = fullscreen ? CaptureSourceMode.FullscreenDesktopDuplication : CaptureSourceMode.Window;
            if (_mode == CaptureSourceMode.FullscreenDesktopDuplication && desiredMode == CaptureSourceMode.Window)
            {
                // Unity and other Direct3D games rebuild their swap chain while
                // leaving exclusive fullscreen. Creating a window capture item
                // during that rebuild can bind to a permanently black surface.
                // Stop display capture before reconnecting the window surface.
                // Detection is sampled; this is not OBS's injected game hook.
                await SuspendCaptureAsync();
                try { await Task.Delay(750, _monitorCancellation.Token); }
                catch (OperationCanceledException) { break; }
                if (!CaptureTargetResolver.TryResolveApplicationWindow(_settings.CaptureApplicationProcessName, _targetProcessId, out var settledTarget) || settledTarget is null) continue;
                var settledScreen = System.Windows.Forms.Screen.FromRectangle(settledTarget.Bounds);
                if (IsCompleteFullscreen(settledTarget.Bounds, settledScreen.Bounds)) continue;
                target = settledTarget;
                screen = settledScreen;
            }
            // WGC emits frames when content changes, so silence is not a failure.
            // Its frame pool resizes internally without replacing the HWND session.
            var failedSource = (_windowFfmpegCapture is { } windowCapture && windowCapture.HasFailed)
                || (_fullscreenCapture is ManagedFfmpegProcess native && (native.Process.HasExited || CaptureTimeline.Seconds(native.LastProgressTimestamp, CaptureTimeline.Now) > 8));
            var sourceChanged = failedSource || !HasActiveSource || desiredMode != _mode
                || (desiredMode == CaptureSourceMode.Window && target.Handle != _targetHandle)
                || (desiredMode == CaptureSourceMode.FullscreenDesktopDuplication && !string.Equals(screen.DeviceName, _displayDeviceName, StringComparison.OrdinalIgnoreCase));
            if (sourceChanged)
            {
                await _gate.WaitAsync();
                try
                {
                    if (_stopped) break;
                    try
                    {
                        try { await StopCurrentPartAsync(); }
                        catch (Exception ex) { Warning?.Invoke(this, ex.Message); }
                        await StartPartAsync(target, desiredMode);
                    }
                    catch (Exception ex)
                    {
                        Warning?.Invoke(this, ex.Message);
                        if (++_restartFailures >= 5)
                        {
                            TargetEnded?.Invoke(this, "アプリ映像の再接続に失敗したため録画を停止します。回復用データを確認してください。");
                            break;
                        }
                        await Task.Delay(1000);
                    }
                }
                finally { _gate.Release(); }
            }

            if (_warningPending && now - _lastSizeChangeUtc >= TimeSpan.FromMilliseconds(650))
            {
                _warningPending = false;
                Warning?.Invoke(this, LocalizationService.ApplicationWindowSizeChanged(_warningStartBounds.Width, _warningStartBounds.Height, _observedBounds.Width, _observedBounds.Height));
            }
        }
    }

    private bool HasActiveSource => _windowFfmpegCapture is not null || _fullscreenCapture is not null;

    private async Task SuspendCaptureAsync()
    {
        if (!HasActiveSource) { _mode = CaptureSourceMode.Suspended; return; }
        await _gate.WaitAsync();
        try
        {
            if (_stopped) return;
            try { await StopCurrentPartAsync(); }
            catch (Exception ex) { Warning?.Invoke(this, ex.Message); }
            _mode = CaptureSourceMode.Suspended;
        }
        finally { _gate.Release(); }
    }

    private async Task RaiseTargetEndedAsync()
    {
        if (_targetEndedRaised) return;
        _targetEndedRaised = true;
        await SuspendCaptureAsync();
        TargetEnded?.Invoke(this, LocalizationService.T("録画対象のアプリが終了したため、録画を自動停止します。"));
    }

    private async Task StopCurrentPartAsync()
    {
        var windowFfmpeg = _windowFfmpegCapture;
        _windowFfmpegCapture = null;
        if (windowFfmpeg is not null)
        {
            try { await windowFfmpeg.StopAsync(); }
            finally { windowFfmpeg.Dispose(); }
        }

        var fullscreen = _fullscreenCapture;
        _fullscreenCapture = null;
        if (fullscreen is not null)
        {
            try { await fullscreen.StopAsync(); }
            finally { fullscreen.Dispose(); }
        }

    }

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        var endedTimestamp = CaptureTimeline.Now;
        _monitorCancellation.Cancel();
        if (_monitorTask is not null) { try { await _monitorTask; } catch (OperationCanceledException) { } }
        await _gate.WaitAsync();
        try { await StopCurrentPartAsync(); }
        finally { _gate.Release(); }

        var validParts = _parts.Where(part => File.Exists(part.Path) && new FileInfo(part.Path).Length > 1024).ToList();
        await VideoPartAssembler.AssembleAsync(_ffmpeg, validParts, endedTimestamp, _outputPath, _targetWidth, _targetHeight, _settings.Quality == VideoQuality.High ? 60 : 30);
        foreach (var part in _parts) RecordingFinalizer.TryDelete(part.Path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_stopped) { try { Task.Run(StopAsync).GetAwaiter().GetResult(); } catch { } }
        _windowFfmpegCapture?.Dispose();
        _windowFfmpegCapture = null;
        _fullscreenCapture?.Dispose();
        _fullscreenCapture = null;
        _monitorCancellation.Dispose();
        _gate.Dispose();
    }

    internal static bool IsCompleteFullscreen(Rectangle windowBounds, Rectangle screenBounds)
    {
        const int tolerance = 12;
        return Math.Abs(windowBounds.Left - screenBounds.Left) <= tolerance
            && Math.Abs(windowBounds.Top - screenBounds.Top) <= tolerance
            && Math.Abs(windowBounds.Right - screenBounds.Right) <= tolerance
            && Math.Abs(windowBounds.Bottom - screenBounds.Bottom) <= tolerance;
    }

    private static bool SizeChanged(Rectangle current, Rectangle previous) => Math.Abs(current.Width - previous.Width) > 4 || Math.Abs(current.Height - previous.Height) > 4;
    private static int MakeEven(int value) => Math.Max(16, value + Math.Abs(value % 2));
}
