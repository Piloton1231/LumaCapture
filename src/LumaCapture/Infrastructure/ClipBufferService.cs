using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed class ClipBufferService : IDisposable
{
    private readonly FfmpegService _ffmpeg = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private const string CacheMarker = ".lumacapture-clip-cache";
    private string _bufferDirectory = AppSettings.DefaultClipCacheDirectory;
    private IManagedCaptureProcess? _video;
    private List<AudioRingTrack> _audio = new();
    private AppSettings? _settings;
    private System.Threading.Timer? _cleanupTimer;
    private int _lowSpaceTriggered;
    private bool _ownsBufferDirectory;
    public bool IsRunning => _video is not null;
    public bool HasBufferedVideo
    {
        get
        {
            try { return Directory.Exists(_bufferDirectory) && Directory.EnumerateFiles(_bufferDirectory, "segment_*.mkv").Any(path => new FileInfo(path).Length > 1024); }
            catch { return false; }
        }
    }
    public event EventHandler<string>? Warning;

    public async Task StartAsync(AppSettings settings)
    {
        await _gate.WaitAsync();
        try
        {
            await StopInternalAsync(true);
            _bufferDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.ClipCacheDirectory));
            CleanupManagedCache(_bufferDirectory, string.Equals(_bufferDirectory, AppSettings.DefaultClipCacheDirectory, StringComparison.OrdinalIgnoreCase));
            if (!settings.ClipBufferEnabled) return;
            _settings = settings.Clone();
            ApplyClipDisplayTarget(_settings);
            Directory.CreateDirectory(_bufferDirectory);
            File.WriteAllText(Path.Combine(_bufferDirectory, CacheMarker), "LumaCapture managed clip cache");
            _ownsBufferDirectory = true;
            await StartVideoInternalAsync();
            try { _audio = await AudioCaptureService.StartRingTracksAsync(_settings, _bufferDirectory, message => Warning?.Invoke(this, message), _video!.StartedTimestamp); }
            catch (Exception ex) { _audio = new List<AudioRingTrack>(); Warning?.Invoke(this, "クリップ用音声を準備できませんでした。映像クリップは保存できます。" + Environment.NewLine + ex.Message); }
            _cleanupTimer = new System.Threading.Timer(_ =>
            {
                if (!_gate.Wait(0)) return;
                try { CleanupOldSegments(); } finally { _gate.Release(); }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }
        finally { _gate.Release(); }
    }

    private static void ApplyClipDisplayTarget(AppSettings settings)
    {
        settings.CaptureApplicationProcessName = string.Empty;
        var selected = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen =>
            string.Equals(screen.DeviceName, settings.ClipDisplayDeviceName, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            settings.CaptureArea = CaptureArea.PrimaryDisplay;
            settings.CaptureDisplayDeviceName = string.Empty;
        }
        else
        {
            settings.CaptureArea = CaptureArea.SpecificDisplay;
            settings.CaptureDisplayDeviceName = selected.DeviceName;
        }
    }

    public async Task<string> SaveClipAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_settings is null || _video is null) throw new InvalidOperationException("クリップ機能が無効か、準備中です。");
            var settings = _settings.Clone();
            var prefix = "Clip_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var jobDirectory = Path.Combine(settings.OutputDirectory, ".LumaCapture-recovery", "clip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(jobDirectory);

            var audioFiles = _audio.Select(track => track.SaveSnapshot(jobDirectory, "audio", settings.ClipSeconds)).ToList();
            await _video.StopAsync();
            _video.Dispose();
            _video = null;
            var maximumSegments = Math.Max(2, (int)Math.Ceiling(settings.ClipSeconds / 2d) + 2);
            var segments = Directory.EnumerateFiles(_bufferDirectory, "segment_*.mkv")
                .Select(path => new FileInfo(path))
                .Where(file => file.Length > 1024)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .TakeLast(maximumSegments)
                .Select(file => file.FullName)
                .ToList();

            // Pin closed segments in this export so rolling cleanup cannot delete
            // inputs while a long export is still reading them.
            for (var index = 0; index < segments.Count; index++)
            {
                var pinned = Path.Combine(jobDirectory, $"source_{index:D5}.mkv");
                File.Copy(segments[index], pinned);
                segments[index] = pinned;
            }
            await StartVideoInternalAsync();
            if (segments.Count == 0) throw new InvalidOperationException("まだ保存できる映像がありません。数秒待ってからもう一度お試しください。");

            try
            {
                var combined = Path.Combine(jobDirectory, "combined.mkv");
                var trimmed = Path.Combine(jobDirectory, "video.mkv");
                var finalizer = new RecordingFinalizer(_ffmpeg);
                await finalizer.ConcatSegmentsAsync(segments, combined);
                try { await finalizer.TrimFromEndAsync(combined, settings.ClipSeconds, trimmed); }
                catch { File.Copy(combined, trimmed, true); }
                if (!File.Exists(trimmed) || new FileInfo(trimmed).Length <= 1024) File.Copy(combined, trimmed, true);
                var output = await finalizer.FinalizeAsync(trimmed, audioFiles, settings, settings.OutputDirectory, prefix);
                try { Directory.Delete(jobDirectory, true); } catch { }
                return output;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(jobDirectory, "failure.log"), ex.ToString()); } catch { }
                throw new InvalidOperationException($"クリップ保存に失敗しました。回復用データ: {jobDirectory}", ex);
            }
            finally
            {
                CleanupOldSegments();
            }
        }
        finally { _gate.Release(); }
    }

    private async Task StartVideoInternalAsync()
    {
        if (_settings is null) return;
        var pattern = Path.Combine(_bufferDirectory, $"segment_{DateTime.UtcNow:yyyyMMddHHmmssfff}_%06d.mkv");
        _video = await _ffmpeg.StartScreenCaptureAsync(_settings, pattern, true);
        if (_video is ResilientDisplayCaptureProcess displayCapture)
            displayCapture.CaptureFailed += (_, message) => { Warning?.Invoke(this, message); _ = Task.Run(StopAsync); };
    }

    private void CleanupOldSegments()
    {
        var settings = _settings;
        if (settings is null) return;
        try
        {
            var free = StorageMonitorService.GetFreeSpaceGb(_bufferDirectory);
            if (free <= settings.AutoStopGb && Interlocked.Exchange(ref _lowSpaceTriggered, 1) == 0)
            {
                Warning?.Invoke(this, $"一時保存先の空き容量が {free:F1} GB になったため、クリップ機能を自動停止します。");
                _ = Task.Run(StopAsync);
                return;
            }
        }
        catch { }
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(settings.ClipSeconds + 15);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_bufferDirectory, "segment_*.mkv"))
            {
                try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await StopInternalAsync(true); }
        finally { _gate.Release(); }
    }

    private async Task StopInternalAsync(bool cleanupCache)
    {
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        Exception? stopError = null;
        if (_video is not null)
        {
            try { await _video.StopAsync(); }
            catch (Exception ex) { stopError = ex; }
            finally { _video.Dispose(); }
            _video = null;
        }
        foreach (var track in _audio) track.Dispose();
        _audio.Clear();
        _settings = null;
        Interlocked.Exchange(ref _lowSpaceTriggered, 0);
        if (cleanupCache && _ownsBufferDirectory) CleanupManagedCache(_bufferDirectory, string.Equals(_bufferDirectory, AppSettings.DefaultClipCacheDirectory, StringComparison.OrdinalIgnoreCase));
        _ownsBufferDirectory = false;
        if (stopError is not null) throw stopError;
    }

    internal static bool HasManagedCacheData(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return false;
            return File.Exists(Path.Combine(directory, CacheMarker))
                || Directory.EnumerateFiles(directory, "segment_*.mkv").Any()
                || Directory.EnumerateFiles(directory, "audio_*.pcm").Any()
                || Directory.EnumerateDirectories(directory, "job_*").Any();
        }
        catch { return true; }
    }

    internal static void CleanupManagedCache(string directory, bool allowUnmarked)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            var marker = Path.Combine(directory, CacheMarker);
            if (!allowUnmarked && !File.Exists(marker)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "segment_*.mkv")) RecordingFinalizer.TryDelete(file);
            foreach (var file in Directory.EnumerateFiles(directory, "segment_*.ffmpeg.log")) RecordingFinalizer.TryDelete(file);
            foreach (var file in Directory.EnumerateFiles(directory, "audio_*.pcm")) RecordingFinalizer.TryDelete(file);
            foreach (var job in Directory.EnumerateDirectories(directory, "job_*"))
            {
                try { Directory.Delete(job, true); } catch { }
            }
            RecordingFinalizer.TryDelete(marker);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch { }
    }

    public void Dispose()
    {
        if (CaptureShutdown.IsEnding) { _cleanupTimer?.Dispose(); return; }
        try { Task.Run(StopAsync).GetAwaiter().GetResult(); } catch { }
        _gate.Dispose();
    }
}
