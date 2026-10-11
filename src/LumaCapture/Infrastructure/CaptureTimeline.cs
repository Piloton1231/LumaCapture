using System.Diagnostics;
using NAudio.Wave;

namespace LumaCapture.Infrastructure;

/// <summary>QPC is shared by WASAPI and Stopwatch; never use local time for media time.</summary>
public static class CaptureTimeline
{
    public static long Now => Stopwatch.GetTimestamp();
    public static long ToHundredNanoseconds(long ticks) => (long)((decimal)ticks * 10_000_000 / Stopwatch.Frequency);
    public static double Seconds(long start, long end) => (end - start) / (double)Stopwatch.Frequency;
}

/// <summary>Place packets by acquisition time, not by callback scheduling time.</summary>
public sealed class AudioPacketTimeline
{
    private readonly int _sampleRate;
    private readonly long _origin;
    private long? _lastDeviceEnd;
    public long FramesWritten { get; private set; }
    public AudioPacketTimeline(int sampleRate, long originHundredNanoseconds)
    { _sampleRate = sampleRate; _origin = originHundredNanoseconds; }

    public (long SilenceFrames, int SkipFrames, int WriteFrames) Place(int frames, long devicePosition, long qpcPosition, bool timestampError, long arrivalHundredNanoseconds)
    {
        long start;
        if (!timestampError && qpcPosition > 0 && qpcPosition <= arrivalHundredNanoseconds + 1_000_000 && qpcPosition >= _origin - 10_000_000)
            start = (long)Math.Round((qpcPosition - _origin) * (_sampleRate / 10_000_000d));
        else if (_lastDeviceEnd is { } previous && devicePosition >= previous && devicePosition - previous < _sampleRate * 10L)
            start = FramesWritten + devicePosition - previous;
        else
            start = Math.Max(FramesWritten, (long)((arrivalHundredNanoseconds - _origin) * (_sampleRate / 10_000_000d)) - frames);
        _lastDeviceEnd = devicePosition + frames;
        // Ignore sub-millisecond timestamp rounding, but correct drift in both directions.
        if (Math.Abs(start - FramesWritten) <= _sampleRate / 1000) start = FramesWritten;
        var silence = Math.Max(0, start - FramesWritten);
        var skip = (int)Math.Min(frames, Math.Max(0, FramesWritten - start));
        var write = frames - skip;
        FramesWritten += silence + write;
        return (silence, skip, write);
    }

    public long PadThrough(long endHundredNanoseconds)
    {
        var target = Math.Max(0, (long)((endHundredNanoseconds - _origin) * (_sampleRate / 10_000_000d)));
        var padding = Math.Max(0, target - FramesWritten);
        FramesWritten += padding;
        return padding;
    }
}

/// <summary>RF64 PCM avoids RIFF's 4 GiB limit. Header is checkpointed for recovery.</summary>
public sealed class LongWaveWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly BinaryWriter _header;
    private readonly int _alignment;
    private long _bytes;
    private long _checkpoint;
    public LongWaveWriter(string path, WaveFormat format) : this(new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 65536), format) { }
    internal LongWaveWriter(Stream stream, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16)
            throw new NotSupportedException("Long recording requires 16-bit PCM.");
        _alignment = format.BlockAlign;
        _stream = stream;
        _header = new BinaryWriter(_stream, System.Text.Encoding.ASCII, true);
        _header.Write("RF64"u8); _header.Write(uint.MaxValue); _header.Write("WAVEds64"u8);
        _header.Write(28u); _header.Write(72L); _header.Write(0L); _header.Write(0L); _header.Write(0u);
        _header.Write("fmt "u8); _header.Write(16u); _header.Write((short)1); _header.Write((short)format.Channels);
        _header.Write(format.SampleRate); _header.Write(format.AverageBytesPerSecond); _header.Write((short)_alignment); _header.Write((short)16);
        _header.Write("data"u8); _header.Write(uint.MaxValue);
    }
    public void Write(ReadOnlySpan<byte> data)
    {
        _stream.Write(data); _bytes += data.Length;
        if (_bytes - _checkpoint >= 1024 * 1024) Flush();
    }
    public void Flush()
    {
        var position = _stream.Position;
        _stream.Position = 20;
        _header.Write(_bytes + 72); _header.Write(_bytes); _header.Write(_bytes / _alignment);
        _stream.Position = position;
        _stream.Flush(); _checkpoint = _bytes;
    }
    public void Dispose() { try { Flush(); } finally { _header.Dispose(); _stream.Dispose(); } }
}
