using System.Diagnostics;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed class RecordingService : IDisposable
{
    private readonly FfmpegService _ffmpeg = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IManagedCaptureProcess? _video;
    private List<AudioTrackRecorder> _audio = new();
    private string? _temporaryDirectory;
    private string? _videoPath;
    private string? _baseName;
    private AppSettings? _settings;
    private Stopwatch? _clock;
    public bool IsProcessing { get; private set; }
    private System.Threading.Timer? _healthTimer;
    public bool IsRecording => _video is not null;
    public TimeSpan Elapsed => _clock?.Elapsed ?? TimeSpan.Zero;
    public event EventHandler<bool>? StateChanged;
    public event EventHandler<string>? Warning;
    public event EventHandler<string>? CaptureWarning;
    public event EventHandler<string>? CaptureTargetEnded;

    public async Task StartAsync(AppSettings settings)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsRecording) return;
            settings.Normalize();
            Directory.CreateDirectory(settings.OutputDirectory);
            _settings = settings.Clone();
            _baseName = "Recording_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _temporaryDirectory = Path.Combine(_settings.OutputDirectory, ".LumaCapture-recovery", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
            File.WriteAllText(Path.Combine(_temporaryDirectory, "session.json"), System.Text.Json.JsonSerializer.Serialize(_settings));
            _videoPath = Path.Combine(_temporaryDirectory, "video.mkv");
            _video = await _ffmpeg.StartScreenCaptureAsync(_settings, _videoPath, false);
            if (_video is FollowingWindowCaptureProcess applicationCapture)
            {
                applicationCapture.Warning += (_, message) => CaptureWarning?.Invoke(this, message);
                applicationCapture.TargetEnded += (_, message) => CaptureTargetEnded?.Invoke(this, message);
            }
            if (_video is ResilientDisplayCaptureProcess displayCapture)
                displayCapture.CaptureFailed += (_, message) => CaptureTargetEnded?.Invoke(this, message);
            try { _audio = await AudioCaptureService.StartFileTracksAsync(_settings, _temporaryDirectory, "audio", message => Warning?.Invoke(this, message), _video.StartedTimestamp); }
            catch (Exception ex) { _audio = new List<AudioTrackRecorder>(); Warning?.Invoke(this, "音声を開始できなかったため、映像のみ録画します。" + Environment.NewLine + ex.Message); }
            _clock = Stopwatch.StartNew();
            _healthTimer = new System.Threading.Timer(_ => { if (_audio.Any(track => track.Failure is not null)) CaptureTargetEnded?.Invoke(this, "音声収録でエラーが発生したため、録画を停止して回復用データを保持します。"); }, null, 1000, 1000);
            StateChanged?.Invoke(this, true);
        }
        catch
        {
            if (_video is not null) { try { await _video.StopAsync(); } catch { } finally { _video.Dispose(); _video = null; } }
            foreach (var track in _audio) track.Dispose();
            _audio.Clear();
            ResetSession(false);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> StopAsync()
    {
        await _gate.WaitAsync();
        var succeeded = false;
        try
        {
            if (_video is null || _settings is null || _videoPath is null || _baseName is null) return null;
            IsProcessing = true;
            _healthTimer?.Dispose(); _healthTimer = null;
            var stopTimestamp = CaptureTimeline.Now;
            _clock?.Stop();
            var video = _video;
            try
            {
                // Request every audio stop BEFORE video shutdown starts expensive remuxing.
                var audioStops = _audio.Select(track => track.StopAsync(stopTimestamp)).ToArray();
                await Task.WhenAll(audioStops.Append(video.StopAsync()));
                var audioFiles = _audio.Where(x => File.Exists(x.FilePath) && new FileInfo(x.FilePath).Length > 80)
                    .Select(x => new AudioTrackFile(x.Label, x.FilePath, x.Label == "Microphone", x.WaveFormat.Channels)).ToList();
                var output = await new RecordingFinalizer(_ffmpeg).FinalizeAsync(_videoPath, audioFiles, _settings, _settings.OutputDirectory, _baseName);
                succeeded = true;
                return output;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(_temporaryDirectory!, "failure.log"), ex.ToString()); } catch { }
                throw new InvalidOperationException($"録画の保存に失敗しました。映像・音声・診断情報を次のフォルダーに保持しています。{Environment.NewLine}{_temporaryDirectory}", ex);
            }
            finally
            {
                video.Dispose(); _video = null;
                foreach (var track in _audio) { try { track.Dispose(); } catch { } }
                _audio.Clear();
                ResetSession(succeeded);
                StateChanged?.Invoke(this, false);
            }
        }
        finally { IsProcessing = false; _gate.Release(); }
    }

    private void ResetSession(bool deleteTemporary)
    {
        if (deleteTemporary && _temporaryDirectory is not null)
        {
            try { Directory.Delete(_temporaryDirectory, true); } catch { }
        }
        _temporaryDirectory = _videoPath = _baseName = null;
        _settings = null; _clock = null;
    }

    internal async Task CheckpointForShutdownAsync()
    {
        _healthTimer?.Dispose();
        var end = CaptureTimeline.Now;
        if (_temporaryDirectory is not null)
            try { File.WriteAllText(Path.Combine(_temporaryDirectory, "shutdown.txt"), "Windows session ended. Original media retained; MP4 export was not attempted during shutdown."); } catch { }
        await Task.WhenAll(_audio.ToArray().Select(async track => { try { await track.StopAsync(end).ConfigureAwait(false); } catch { } })).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (CaptureShutdown.IsEnding) return; // Session-ending checkpoint owns cleanup.
        try { Task.Run(StopAsync).GetAwaiter().GetResult(); } catch { }
        foreach (var track in _audio) track.Dispose();
        _audio.Clear();
        _healthTimer?.Dispose();
        _gate.Dispose();
    }
}
