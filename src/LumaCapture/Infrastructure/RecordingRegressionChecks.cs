using System.Diagnostics;
using NAudio.Wave;
using LumaCapture.Models;
using LumaCapture.Windows;

namespace LumaCapture.Infrastructure;

internal static class RecordingRegressionChecks
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        foreach (var ppm in new[] { -200d, 0d, 200d })
        {
            const long origin = 2_000_000_000;
            var timeline = new AudioPacketTimeline(48000, origin);
            long expected = 0;
            // Six hours in timestamp space, with varying callback latency and
            // clocks that run 200 ppm faster/slower than the capture clock.
            for (var packet = 0; packet < 2_160_000; packet++)
            {
                var seconds = packet * .01 / (1 + ppm / 1_000_000);
                var qpc = origin + (long)Math.Round(seconds * 10_000_000);
                timeline.Place(480, packet * 480L, qpc, false, qpc + 100_000 + packet % 17 * 90_000);
                expected = (long)Math.Round(seconds * 48000) + 480;
            }
            var error = Math.Abs(timeline.FramesWritten - expected);
            if (error > 48) throw new Exception($"Six-hour drift {ppm} ppm: {error} frames");
            results.Add($"6h simulated, {ppm:+0;-0;0} ppm: error {error / 48d:F3} ms");
        }
        var gap = new AudioPacketTimeline(48000, 100_000_000);
        gap.Place(480, 0, 100_000_000, false, 100_100_000);
        var afterGap = gap.Place(480, 48000, 110_000_000, false, 110_100_000);
        if (afterGap.SilenceFrames != 47520) throw new Exception("Capture gap compressed time");
        var duplicate = gap.Place(480, 48000, 110_000_000, false, 110_200_000);
        if (duplicate.WriteFrames != 0) throw new Exception("Duplicate packet extended time");
        results.Add("Missing / duplicate packets: PASS");

        var counter = new HeaderOnlyStream();
        using (var writer = new LongWaveWriter(counter, new WaveFormat(48000, 16, 2)))
        {
            var block = new byte[1024 * 1024];
            for (var i = 0; i < 4097; i++) writer.Write(block);
            writer.Flush();
            var bytes = 4097L * block.Length;
            if (BitConverter.ToInt64(counter.Header, 28) != bytes || BitConverter.ToInt64(counter.Header, 36) != bytes / 4)
                throw new Exception("RF64 4 GiB rollover");
        }
        results.Add("RF64 >4 GiB header/count test (virtual stream): PASS");

        var ffmpeg = new FfmpegService();
        var wave = Path.Combine(directory, "rf64.wav");
        using (var writer = new LongWaveWriter(wave, new WaveFormat(48000, 16, 2))) writer.Write(new byte[48000 * 4]);
        await ffmpeg.RunAsync(new[] { "-v", "error", "-i", wave, "-f", "null", "-" });
        results.Add("RF64 actual file decoded by FFmpeg: PASS");

        var first = Path.Combine(directory, "part-red.mkv");
        var second = Path.Combine(directory, "part-blue.mkv");
        foreach (var item in new[] { (first, "red"), (second, "blue") })
            await ffmpeg.RunAsync(new[] { "-v", "error", "-y", "-f", "lavfi", "-i", $"color=c={item.Item2}:s=160x90:r=30:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", item.Item1 });
        var originTicks = CaptureTimeline.Now;
        var assembled = Path.Combine(directory, "reconnect-timeline.mkv");
        await VideoPartAssembler.AssembleAsync(ffmpeg, new[] { new VideoTimelinePart(first, originTicks), new VideoTimelinePart(second, originTicks + Stopwatch.Frequency * 3) }, originTicks + Stopwatch.Frequency * 5, assembled, 160, 90, 30);
        var framePath = Path.Combine(directory, "after-reconnect.png");
        await ffmpeg.RunAsync(new[] { "-v", "error", "-y", "-ss", "3.5", "-i", assembled, "-frames:v", "1", framePath });
        using (var bitmap = new System.Drawing.Bitmap(framePath))
            if (bitmap.GetPixel(80, 45).B < 200) throw new Exception("Reconnect shortened the wall-clock timeline");
        results.Add("Sequential reconnect assembly / preserved 2s gap: PASS");
        var invalidAudio = Path.Combine(directory, "invalid-audio.wav");
        await File.WriteAllTextAsync(invalidAudio, "intentional invalid media for recovery regression");
        var failed = false;
        try { await new RecordingFinalizer(ffmpeg).FinalizeAsync(assembled, new[] { new AudioTrackFile("SystemAudio", invalidAudio) }, new AppSettings(), directory, "must-not-publish"); }
        catch { failed = true; }
        if (!failed || !File.Exists(assembled) || !File.Exists(invalidAudio) || File.Exists(Path.Combine(directory, "must-not-publish.mp4"))) throw new Exception("Failed export lost sources or published incomplete output");
        results.Add("Intentional export failure preserves originals / no incomplete publication: PASS");

        var language = LocalizationService.CurrentLanguage;
        var installer = new InstallerWindow(InstallerMode.Install);
        installer.Show();
        foreach (var choice in Enum.GetValues<AppLanguage>())
        {
            installer.SelectLanguageForTest(choice);
            var expected = LocalizationService.T("インストール");
            var button = (System.Windows.Controls.Button)installer.FindName("PrimaryButton");
            if (!Equals(button.Content, expected)) throw new Exception("Installer translation failed: " + choice);
            installer.Measure(new System.Windows.Size(680, 560));
            installer.Arrange(new System.Windows.Rect(0, 0, 680, 560));
            installer.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(680, 560, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(installer);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"installer-{choice}.png"));
            encoder.Save(file);
        }
        installer.Close(); LocalizationService.SetLanguage(language);
        results.Add("Installer: Japanese / English / Korean / Simplified & Traditional Chinese: PASS");
        await File.WriteAllLinesAsync(Path.Combine(directory, "regression-results.txt"), results);
    }

    public static async Task RunEnduranceAsync(string directory, int seconds)
    {
        Directory.CreateDirectory(directory);
        var settings = new AppSettings { OutputDirectory = directory, Quality = VideoQuality.Compact, Resolution = VideoResolution.P720, CaptureArea = CaptureArea.PrimaryDisplay, CaptureCursor = false, ClipBufferEnabled = false };
        using var recording = new RecordingService();
        await recording.StartAsync(settings);
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var started = Stopwatch.StartNew();
        var output = await recording.StopAsync();
        await File.WriteAllTextAsync(Path.Combine(directory, "endurance-result.txt"), $"Requested: {seconds}s\nOutput: {output}\nFinalization: {started.Elapsed}\n");
    }

    private sealed class HeaderOnlyStream : Stream
    {
        public byte[] Header { get; } = new byte[80];
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get; set; }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Position < Header.Length) buffer[..(int)Math.Min(buffer.Length, Header.Length - Position)].CopyTo(Header.AsSpan((int)Position));
            Position += buffer.Length; _length = Math.Max(_length, Position);
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, _ => Length + offset };
        public override void SetLength(long value) => _length = value;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
