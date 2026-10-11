using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaCapture.Models;
using LumaCapture.Windows;

namespace LumaCapture.Infrastructure;

internal static class DashboardChecks
{
    public static async Task SettingsSaveAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var scenario in new[] { "system-empty", "selected-empty", "selected-offline", "explicit-clear" })
        {
            var settings = new AppSettings { OutputDirectory = Path.Combine(directory, "videos"), ClipCacheDirectory = Path.Combine(directory, "cache"), AudioCaptureMode = scenario == "system-empty" ? AudioCaptureMode.SystemAudio : AudioCaptureMode.SelectedApplications, Resolution = VideoResolution.P720 };
            if (scenario is "selected-offline" or "explicit-clear") settings.SelectedApplications.Add("LumaCapture_NotRunning_TestApp");
            var window = new SettingsWindow(settings);
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                if (scenario == "explicit-clear") foreach (var box in ((StackPanel)window.FindName("ProcessList")).Children.OfType<CheckBox>()) box.IsChecked = false;
                window.RefreshForTest(); window.SaveForTest();
                if (window.ResultSettings is null) window.Close();
            });
            window.ShowDialog();
            var saved = window.ResultSettings ?? throw new Exception("Save blocked: " + scenario + ": " + ((TextBlock)window.FindName("ValidationText")).Text);
            if (saved.AudioCaptureMode != settings.AudioCaptureMode || saved.Resolution != VideoResolution.P720) throw new Exception("Settings altered unexpectedly");
            if (scenario == "selected-offline" ? !saved.SelectedApplications.Contains("LumaCapture_NotRunning_TestApp") : saved.SelectedApplications.Count != 0) throw new Exception("Audio selections lost/restored unexpectedly: " + scenario);
        }
        var audioSettings = new AppSettings { AudioCaptureMode = AudioCaptureMode.SelectedApplications };
        var tracks = await AudioCaptureService.StartFileTracksAsync(audioSettings, directory, "empty");
        if (tracks.Count != 0) { foreach (var track in tracks) track.Dispose(); throw new Exception("Empty selection recorded unintended audio"); }
        var microphone = AudioCaptureService.ListMicrophones().FirstOrDefault();
        var micResult = "SKIP: no microphone available";
        if (microphone is not null)
        {
            audioSettings.MicrophoneDeviceId = microphone.Id;
            tracks = await AudioCaptureService.StartFileTracksAsync(audioSettings, directory, "microphone-only");
            try
            {
                if (tracks.Count != 1 || tracks[0].Label != "Microphone") throw new Exception("Empty app selection prevented microphone capture");
                await Task.Delay(300);
                await tracks[0].StopAsync(); micResult = "PASS: microphone-only capture with no audio apps";
            }
            finally { foreach (var track in tracks) track.Dispose(); }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "settings-save-result.txt"), "PASS: system/selected empty save, offline selection retained, explicit clear retained, no all-PC fallback.\n" + micResult);
    }
    public static async Task RenderAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            LocalizationService.SetLanguage(language);
            var settings = new AppSettings { Language = language, OutputDirectory = @"C:\Videos\LumaCapture" };
            var panel = new CapturePanelWindow(settings.Quality) { AutoHide = false };
            panel.UpdateConfiguration(settings, true); panel.Show();
            var starts = 0; panel.RecordToggleRequested += (_, _) => starts++;
            ((Button)panel.FindName("RecordButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (starts != 1) throw new Exception("Record button event not connected");
            await Task.Delay(100);
            Save(panel, Path.Combine(directory, $"dashboard-{language}.png"));
            panel.UpdateRecording(true, TimeSpan.FromSeconds(83));
            if (((Button)panel.FindName("AppPickerButton")).IsEnabled) throw new Exception("Target change enabled while recording");
            Save(panel, Path.Combine(directory, $"recording-{language}.png"));
            panel.SetBusy(true); panel.UpdateRecording(false, TimeSpan.Zero);
            if (((Button)panel.FindName("RecordButton")).IsEnabled) throw new Exception("Duplicate start enabled while processing");
            panel.Close();
            var window = new SettingsWindow(settings); window.Show();
            var tabs = (TabControl)window.FindName("SettingsTabs");
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i; await Task.Delay(60);
                Save(window, Path.Combine(directory, $"settings-{language}-{i}.png"));
            }
            window.Close();
            var picker = new ApplicationPickerWindow(""); picker.Show(); await Task.Delay(60);
            var search = (TextBox)picker.FindName("SearchBox");
            search.Text = "__LumaCapture_missing_app_test__";
            if (((StackPanel)picker.FindName("ProcessPanel")).Children.OfType<Button>().Any(button => button.Visibility == Visibility.Visible)) throw new Exception("App search did not filter results");
            search.Text = "";
            Save(picker, Path.Combine(directory, $"picker-{language}.png")); picker.Close();
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "ui-result.txt"), "PASS: 5 languages, dashboard idle/recording/busy state, all settings tabs rendered.");
    }
    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    public static async Task ShutdownAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var ffmpeg = new FfmpegService();
        // A separately owned test process proves shutdown does not kill by name.
        var info = new ProcessStartInfo(ffmpeg.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-re", "-f", "lavfi", "-i", "color=size=64x64:rate=1", "-t", "30", "-f", "null", "-" }) info.ArgumentList.Add(arg);
        using var unrelated = Process.Start(info)!;
        using var recording = new RecordingService();
        try
        {
            await recording.StartAsync(new AppSettings { OutputDirectory = directory, Resolution = VideoResolution.P720, Quality = VideoQuality.Compact, CaptureCursor = false });
            await Task.Delay(1200);
            var timer = Stopwatch.StartNew(); CaptureShutdown.Begin();
            await Task.WhenAll(CaptureShutdown.StopAsync(), recording.CheckpointForShutdownAsync()).WaitAsync(TimeSpan.FromSeconds(4));
            if (unrelated.HasExited) throw new Exception("Unrelated test process was terminated");
            if (CaptureShutdown.ActiveChildren != 0) throw new Exception("Owned capture process survived shutdown");
            var refused = false;
            try { await ffmpeg.RunAsync(new[] { "-version" }); } catch (OperationCanceledException) { refused = true; }
            if (!refused) throw new Exception("Capture restarted after session ending");
            if (!Directory.EnumerateFiles(directory, "shutdown.txt", SearchOption.AllDirectories).Any()) throw new Exception("Recovery marker missing");
            var media = Directory.EnumerateFiles(directory, "*.mkv", SearchOption.AllDirectories).ToArray();
            if (media.Length == 0 || media.All(path => new FileInfo(path).Length == 0)) throw new Exception("Shutdown discarded recording");
            await File.WriteAllTextAsync(Path.Combine(directory, "shutdown-result.txt"), $"PASS: session-end cleanup {timer.Elapsed}; restart blocked; unrelated process survived; recovery media retained. Simulated handler, no Windows shutdown performed.");
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); }
    }
}
