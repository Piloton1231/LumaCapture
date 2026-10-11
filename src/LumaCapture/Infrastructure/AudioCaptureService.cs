using System.Diagnostics;
using LumaCapture.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LumaCapture.Infrastructure;

public static class AudioCaptureService
{
    private static readonly WaveFormat CaptureFormat = new(48000, 16, 2);

    public static IReadOnlyList<MicrophoneDeviceInfo> ListMicrophones()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try { using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); defaultId = defaultDevice.ID; } catch { }
        var result = new List<MicrophoneDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            try { result.Add(new MicrophoneDeviceInfo(device.ID, device.FriendlyName, string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase))); }
            finally { device.Dispose(); }
        }
        return result.OrderByDescending(device => device.IsDefault).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static async Task<List<AudioTrackRecorder>> StartFileTracksAsync(AppSettings settings, string directory, string prefix, Action<string>? warning = null, long? originTimestamp = null)
    {
        Directory.CreateDirectory(directory);
        var tracks = new List<AudioTrackRecorder>();
        AudioTrackRecorder? microphoneTrack = null;
        try
        {
            if (settings.AudioCaptureMode == AudioCaptureMode.SystemAudio)
            {
                var recorder = await CreateSystemRecorderAsync();
                tracks.Add(new AudioTrackRecorder("SystemAudio", Path.Combine(directory, $"{prefix}_system.wav"), recorder));
            }
            else
            {
                var processes = ProcessCatalog.ResolveSelected(settings.SelectedApplications);
                if (processes.Count == 0) warning?.Invoke(LocalizationService.T("録音対象のアプリがないため、PC・アプリ音声は収録しません。マイクは選択済みの場合のみ収録します。"));
                foreach (var process in processes)
                {
                    var recorder = await CreateProcessRecorderAsync(process.ProcessId);
                    tracks.Add(new AudioTrackRecorder(process.ProcessName, Path.Combine(directory, $"{prefix}_{ProcessCatalog.SafeLabel(process.ProcessName)}.wav"), recorder));
                }
            }
            if (!string.IsNullOrWhiteSpace(settings.MicrophoneDeviceId))
            {
                try
                {
                    var recorder = await CreateMicrophoneRecorderAsync(settings.MicrophoneDeviceId);
                    microphoneTrack = new AudioTrackRecorder("Microphone", Path.Combine(directory, $"{prefix}_microphone.wav"), recorder);
                    tracks.Add(microphoneTrack);
                }
                catch (Exception ex) { warning?.Invoke("選択したマイクを開始できませんでした。PC・アプリ音声のみ録音します。" + Environment.NewLine + ex.Message); }
            }
            foreach (var track in tracks.ToList())
            {
                try { track.Start(originTimestamp); }
                catch (Exception ex) when (ReferenceEquals(track, microphoneTrack))
                {
                    track.Dispose(); tracks.Remove(track);
                    warning?.Invoke("選択したマイクを開始できませんでした。PC・アプリ音声のみ録音します。" + Environment.NewLine + ex.Message);
                }
            }
            return tracks;
        }
        catch
        {
            foreach (var track in tracks) track.Dispose();
            throw;
        }
    }

    public static async Task<List<AudioRingTrack>> StartRingTracksAsync(AppSettings settings, string directory, Action<string>? warning = null, long? originTimestamp = null)
    {
        Directory.CreateDirectory(directory);
        var tracks = new List<AudioRingTrack>();
        AudioRingTrack? microphoneTrack = null;
        try
        {
            if (settings.AudioCaptureMode == AudioCaptureMode.SystemAudio)
            {
                var recorder = await CreateSystemRecorderAsync();
                tracks.Add(new AudioRingTrack("SystemAudio", Path.Combine(directory, "audio_system.pcm"), settings.ClipSeconds + 20, recorder));
            }
            else
            {
                var processes = ProcessCatalog.ResolveSelected(settings.SelectedApplications);
                foreach (var process in processes.Take(8))
                {
                    var recorder = await CreateProcessRecorderAsync(process.ProcessId);
                    tracks.Add(new AudioRingTrack(process.ProcessName, Path.Combine(directory, $"audio_{process.ProcessId}.pcm"), settings.ClipSeconds + 20, recorder));
                }
            }
            if (!string.IsNullOrWhiteSpace(settings.MicrophoneDeviceId))
            {
                try
                {
                    var recorder = await CreateMicrophoneRecorderAsync(settings.MicrophoneDeviceId);
                    microphoneTrack = new AudioRingTrack("Microphone", Path.Combine(directory, "audio_microphone.pcm"), settings.ClipSeconds + 20, recorder);
                    tracks.Add(microphoneTrack);
                }
                catch (Exception ex) { warning?.Invoke("選択したマイクをクリップ用に開始できませんでした。PC・アプリ音声のみ保持します。" + Environment.NewLine + ex.Message); }
            }
            foreach (var track in tracks.ToList())
            {
                try { track.Start(originTimestamp); }
                catch (Exception ex) when (ReferenceEquals(track, microphoneTrack))
                {
                    track.Dispose(); tracks.Remove(track);
                    warning?.Invoke("選択したマイクをクリップ用に開始できませんでした。PC・アプリ音声のみ保持します。" + Environment.NewLine + ex.Message);
                }
            }
            return tracks;
        }
        catch
        {
            foreach (var track in tracks) track.Dispose();
            throw;
        }
    }

    private static Task<WasapiRecorder> CreateSystemRecorderAsync() =>
        new WasapiRecorderBuilder().WithLoopbackCapture().WithFormat(CaptureFormat).WithBufferLength(100).BuildAsync();

    private static Task<WasapiRecorder> CreateProcessRecorderAsync(int processId) =>
        new WasapiRecorderBuilder().WithProcessLoopback((uint)processId, ProcessLoopbackMode.IncludeTargetProcessTree).WithFormat(CaptureFormat).WithBufferLength(100).BuildAsync();

    private static async Task<WasapiRecorder> CreateMicrophoneRecorderAsync(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        return await new WasapiRecorderBuilder().WithDevice(device).WithFormat(CaptureFormat).WithBufferLength(100).BuildAsync();
    }
}

public sealed record MicrophoneDeviceInfo(string Id, string Name, bool IsDefault);

public sealed class AudioTrackRecorder : IDisposable
{
    private static readonly byte[] Zeros = new byte[32768];
    private readonly WasapiRecorder _recorder;
    private readonly LongWaveWriter _writer;
    private readonly System.Threading.Channels.Channel<Packet> _packets = System.Threading.Channels.Channel.CreateBounded<Packet>(256);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AudioPacketTimeline? _timeline;
    private Task? _writing;
    private long _endTimestamp;
    private Exception? _failure;
    public Exception? Failure => _failure;
    private bool _disposed;
    private readonly WaveFormat _format;
    public string Label { get; }
    public string FilePath { get; }
    public WaveFormat WaveFormat => _format;
    private sealed record Packet(byte[] Bytes, AudioClientBufferFlags Flags, long Device, long Qpc, long Arrival);

    public AudioTrackRecorder(string label, string filePath, WasapiRecorder recorder)
    {
        Label = label; FilePath = filePath; _recorder = recorder; _format = recorder.WaveFormat;
        _writer = new LongWaveWriter(filePath, _format);
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += (_, e) => { if (e.Exception is not null) _failure = e.Exception; _stopped.TrySetResult(); };
    }

    public void Start(long? originTimestamp = null)
    {
        _timeline = new AudioPacketTimeline(_format.SampleRate, CaptureTimeline.ToHundredNanoseconds(originTimestamp ?? CaptureTimeline.Now));
        _writing = Task.Run(WritePacketsAsync);
        _recorder.StartRecording();
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (_disposed || _failure is not null) return;
        var packet = new Packet(data.ToArray(), flags, devicePosition, qpcPosition, CaptureTimeline.ToHundredNanoseconds(CaptureTimeline.Now));
        if (!_packets.Writer.TryWrite(packet))
            _failure = new IOException("Audio storage cannot keep up with capture. Original recording data has been retained.");
    }

    private async Task WritePacketsAsync()
    {
        try
        {
            await foreach (var packet in _packets.Reader.ReadAllAsync())
            {
                var frames = packet.Bytes.Length / _format.BlockAlign;
                var placement = _timeline!.Place(frames, packet.Device, packet.Qpc, ((int)packet.Flags & 4) != 0, packet.Arrival);
                WriteSilence(placement.SilenceFrames);
                if (((int)packet.Flags & 2) != 0) WriteSilence(placement.WriteFrames);
                else _writer.Write(packet.Bytes.AsSpan(placement.SkipFrames * _format.BlockAlign, placement.WriteFrames * _format.BlockAlign));
            }
            WriteSilence(_timeline!.PadThrough(CaptureTimeline.ToHundredNanoseconds(_endTimestamp)));
            _writer.Flush();
        }
        catch (Exception ex) { _failure = ex; }
    }

    private void WriteSilence(long frames)
    {
        var bytes = checked(frames * _format.BlockAlign);
        while (bytes > 0) { var count = (int)Math.Min(bytes, Zeros.Length - Zeros.Length % _format.BlockAlign); _writer.Write(Zeros.AsSpan(0, count)); bytes -= count; }
    }

    public async Task StopAsync(long? endTimestamp = null)
    {
        if (_disposed) return;
        _endTimestamp = endTimestamp ?? CaptureTimeline.Now;
        try
        {
            _recorder.StopRecording();
            if (await Task.WhenAny(_stopped.Task, Task.Delay(5000)) != _stopped.Task)
                _failure ??= new TimeoutException("Audio device did not stop within five seconds.");
        }
        finally
        {
            _packets.Writer.TryComplete();
            if (_writing is not null) await _writing;
            Dispose();
        }
        if (_failure is not null) throw new IOException("Audio capture failed; recovery files have been retained.", _failure);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _recorder.StopRecording(); } catch { }
        _packets.Writer.TryComplete();
        if (_writing is not null && !_writing.IsCompleted)
        {
            _endTimestamp = CaptureTimeline.Now;
            _writing.GetAwaiter().GetResult();
        }
        try { _writer.Dispose(); } finally { _recorder.Dispose(); }
    }
}

public sealed class AudioRingTrack : IDisposable
{
    private static readonly byte[] Zeros = new byte[32768];
    private readonly object _gate = new();
    private readonly WasapiRecorder _recorder;
    private readonly CircularFileBuffer _buffer;
    private AudioPacketTimeline? _timeline;
    private bool _disposed;
    public string Label { get; }
    public WaveFormat WaveFormat => _recorder.WaveFormat;

    public AudioRingTrack(string label, string bufferPath, int seconds, WasapiRecorder recorder)
    {
        Label = label;
        _recorder = recorder;
        _buffer = new CircularFileBuffer(bufferPath, (long)recorder.WaveFormat.AverageBytesPerSecond * seconds, recorder.WaveFormat.BlockAlign);
        _recorder.DataAvailable += OnDataAvailable;
    }

    public void Start(long? originTimestamp = null) { _timeline = new AudioPacketTimeline(WaveFormat.SampleRate, CaptureTimeline.ToHundredNanoseconds(originTimestamp ?? CaptureTimeline.Now)); _recorder.StartRecording(); }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var placement = _timeline!.Place(data.Length / WaveFormat.BlockAlign, devicePosition, qpcPosition, ((int)flags & 4) != 0, CaptureTimeline.ToHundredNanoseconds(CaptureTimeline.Now));
            WriteSilence(placement.SilenceFrames);
            if (((int)flags & 2) != 0) WriteSilence(placement.WriteFrames);
            else _buffer.Write(data.Slice(placement.SkipFrames * WaveFormat.BlockAlign, placement.WriteFrames * WaveFormat.BlockAlign));
        }
    }

    private void WriteSilence(long frames)
    {
        var bytes = frames * WaveFormat.BlockAlign;
        while (bytes > 0)
        {
            var count = (int)Math.Min(bytes, Zeros.Length - Zeros.Length % WaveFormat.BlockAlign);
            _buffer.Write(Zeros.AsSpan(0, count)); bytes -= count;
        }
    }

    public AudioTrackFile SaveSnapshot(string directory, string prefix, int seconds)
    {
        lock (_gate)
        {
            WriteSilence(_timeline!.PadThrough(CaptureTimeline.ToHundredNanoseconds(CaptureTimeline.Now)));
            var bytes = _buffer.Snapshot((long)WaveFormat.AverageBytesPerSecond * seconds);
            var path = Path.Combine(directory, $"{prefix}_{ProcessCatalog.SafeLabel(Label)}.wav");
            using var writer = new WaveFileWriter(path, WaveFormat);
            writer.Write(bytes);
            return new AudioTrackFile(Label, path, Label == "Microphone", WaveFormat.Channels);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        // WASAPI shutdown may join its callback thread. Never hold the lock
        // that a pending callback needs while waiting for that shutdown.
        try { _recorder.StopRecording(); } catch { }
        _recorder.Dispose();
        lock (_gate) _buffer.Dispose();
    }
}

internal sealed class CircularFileBuffer : IDisposable
{
    private readonly FileStream _stream;
    private readonly long _capacity;
    private readonly int _alignment;
    private long _position;
    private long _length;

    public CircularFileBuffer(string path, long capacity, int alignment)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _alignment = Math.Max(1, alignment);
        _capacity = Math.Max(_alignment, capacity - capacity % _alignment);
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 65536, FileOptions.RandomAccess);
        _stream.SetLength(_capacity);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var count = data.Length - data.Length % _alignment;
        if (count <= 0) return;
        if (count > _capacity)
        {
            var keep = (int)Math.Min(int.MaxValue, _capacity);
            data = data.Slice(count - keep, keep);
            count = data.Length;
        }
        var first = (int)Math.Min(count, _capacity - _position);
        _stream.Position = _position;
        _stream.Write(data[..first]);
        if (first < count)
        {
            _stream.Position = 0;
            _stream.Write(data[first..count]);
        }
        _position = (_position + count) % _capacity;
        _length = Math.Min(_capacity, _length + count);
    }

    public byte[] Snapshot(long requested)
    {
        var countLong = Math.Min(_length, requested);
        countLong -= countLong % _alignment;
        if (countLong > int.MaxValue) throw new InvalidOperationException("音声クリップが大きすぎます。");
        var count = (int)countLong;
        var result = new byte[count];
        var start = (_position - count + _capacity) % _capacity;
        var first = (int)Math.Min(count, _capacity - start);
        _stream.Flush();
        _stream.Position = start;
        _stream.ReadExactly(result.AsSpan(0, first));
        if (first < count)
        {
            _stream.Position = 0;
            _stream.ReadExactly(result.AsSpan(first));
        }
        return result;
    }

    public void Dispose() => _stream.Dispose();
}
