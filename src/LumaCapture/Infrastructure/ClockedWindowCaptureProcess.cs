using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

/// <summary>WGC produces changed frames; a monotonic clock drives encoding even
/// for static windows. Source shutdown cannot block encoder finalization.</summary>
internal sealed class ClockedWindowCaptureProcess : IManagedCaptureProcess
{
    private readonly object _frameGate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Process _source;
    private readonly SafeFileHandle? _sourceJob;
    private readonly Task _readTask;
    private readonly Task _errorTask;
    private readonly System.Text.StringBuilder _errors = new();
    private readonly int _frameBytes;
    private readonly int _fps;
    private readonly string _outputPath;
    private byte[]? _latest;
    private ManagedFfmpegProcess? _encoder;
    private Task? _pump;
    private Exception? _failure;
    private bool _stopped;
    private bool _disposed;
    public long StartedTimestamp { get; private set; }
    public bool HasFailed => _failure is not null || _source.HasExited || (_encoder?.Process.HasExited ?? false);

    private ClockedWindowCaptureProcess(FfmpegService ffmpeg, AppSettings settings, IntPtr hwnd, string output, int width, int height)
    {
        _frameBytes = checked(width * height * 4);
        _fps = settings.Quality == VideoQuality.High ? 60 : 30;
        _outputPath = output;
        var graph = $"gfxcapture=hwnd={hwnd.ToInt64()}:capture_cursor={(settings.CaptureCursor ? 1 : 0)}:capture_border=0:display_border=0:max_framerate={_fps}:width={width}:height={height}:resize_mode=scale_aspect,hwdownload,format=bgra[out]";
        var info = new ProcessStartInfo(ffmpeg.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "warning", "-filter_complex", graph, "-map", "[out]", "-an", "-c:v", "rawvideo", "-threads:v", "1", "-fps_mode", "passthrough", "-flush_packets", "1", "-f", "rawvideo", "pipe:1" }) info.ArgumentList.Add(arg);
        _source = CaptureShutdown.Start(info, child => { if (!child.HasExited) child.Kill(true); });
        _sourceJob = FfmpegChildProcessJob.TryAttach(_source);
        _errorTask = DrainErrorsAsync();
        _readTask = Task.Run(ReadFramesAsync);
    }

    public static async Task<ClockedWindowCaptureProcess> StartAsync(FfmpegService ffmpeg, AppSettings settings, IntPtr hwnd, string output, int width, int height)
    {
        var capture = new ClockedWindowCaptureProcess(ffmpeg, settings, hwnd, output, width, height);
        try
        {
            await capture._firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(8));
            foreach (var hardware in new[] { true, false })
            {
                var encoder = ffmpeg.StartRawVideoEncoder(settings, output, width, height, hardware);
                capture.StartedTimestamp = CaptureTimeline.Now;
                var frame = new byte[capture._frameBytes];
                lock (capture._frameGate) capture._latest!.CopyTo(frame, 0);
                try
                {
                    await encoder.Process.StandardInput.BaseStream.WriteAsync(frame).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    await encoder.Process.StandardInput.BaseStream.FlushAsync();
                    await Task.Delay(300);
                    if (encoder.Process.HasExited) throw new IOException(await encoder.ReadErrorAsync());
                    capture._encoder = encoder;
                    capture._pump = Task.Run(capture.PumpAsync);
                    return capture;
                }
                catch
                {
                    await encoder.StopAsync(); encoder.Dispose();
                    if (!hardware) throw;
                }
            }
            throw new IOException("Cannot start video encoder.");
        }
        catch { capture.Dispose(); throw; }
    }

    private async Task DrainErrorsAsync()
    {
        while (await _source.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            lock (_errors) { _errors.AppendLine(line); if (_errors.Length > 32768) _errors.Remove(0, _errors.Length - 32768); }
    }

    private async Task ReadFramesAsync()
    {
        var next = new byte[_frameBytes];
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                await _source.StandardOutput.BaseStream.ReadExactlyAsync(next, _cancellation.Token).ConfigureAwait(false);
                lock (_frameGate)
                {
                    var previous = _latest; _latest = next; next = previous ?? new byte[_frameBytes];
                }
                _firstFrame.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (EndOfStreamException ex)
        {
            // Closing a captured HWND ends the WGC stream normally. The owner
            // observes process exit and target identity; already captured frames
            // must still be finalized rather than making target-exit handling fail.
            if (!_firstFrame.Task.IsCompletedSuccessfully) { _failure = ex; _firstFrame.TrySetException(ex); }
        }
        catch (Exception ex) { if (!_cancellation.IsCancellationRequested) { _failure = ex; _firstFrame.TrySetException(ex); } }
    }

    private async Task PumpAsync()
    {
        var frame = new byte[_frameBytes];
        long written = 1;
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var due = (long)(CaptureTimeline.Seconds(StartedTimestamp, CaptureTimeline.Now) * _fps);
                if (written > due) { await Task.Delay(2, _cancellation.Token).ConfigureAwait(false); continue; }
                if (due - written > _fps * 3) throw new IOException("Video encoding fell more than three seconds behind real time. Capture stopped to prevent audio drift.");
                lock (_frameGate) _latest!.CopyTo(frame, 0);
                // Finish a complete raw frame before closing stdin; cancellation
                // in the middle of a frame corrupts the encoder's final packet.
                await _encoder!.Process.StandardInput.BaseStream.WriteAsync(frame).AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                written++;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (!_cancellation.IsCancellationRequested) _failure = ex; }
    }

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        _cancellation.Cancel();
        // Only this owned raw producer is terminated; it has no file to finalize.
        try { if (!_source.HasExited) _source.Kill(true); } catch { }
        if (_pump is not null) await _pump.ConfigureAwait(false);
        await _readTask.ConfigureAwait(false);
        await _source.WaitForExitAsync().ConfigureAwait(false);
        await _errorTask.ConfigureAwait(false);
        if (_encoder is not null) await _encoder.StopAsync().ConfigureAwait(false);
        if (_failure is not null) throw new IOException("Application capture failed; completed video data is retained.", _failure);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_stopped) { try { Task.Run(StopAsync).GetAwaiter().GetResult(); } catch { } }
        _encoder?.Dispose();
        lock (_errors) { if (_errors.Length > 0) { try { File.WriteAllText(_outputPath + ".capture.log", _errors.ToString()); } catch { } } }
        CaptureShutdown.Forget(_source);
        _source.Dispose(); _sourceJob?.Dispose(); _cancellation.Dispose();
    }
}
