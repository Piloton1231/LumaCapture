using System.Threading;
using System.Windows;
using LumaCapture.Infrastructure;
using LumaCapture.Models;
using LumaCapture.Windows;

namespace LumaCapture;

public partial class App : Application
{
    private const string MutexName = @"Local\LumaCapture.Singleton";
    private const string ShowEventName = @"Local\LumaCapture.Show";
    private Mutex? _singleInstance;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showCancellation;
    private AppController? _controller;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Receive session-ending before child encoders at Windows' default 0x280.
        CaptureShutdown.ConfigureCurrentProcess();
        SessionEnding += (_, _) =>
        {
            CaptureShutdown.Begin();
            _controller?.BeginSystemShutdown();
            // Never open a confirmation dialog or perform a long MP4 export here.
            try { Task.Run(async () => await Task.WhenAll(CaptureShutdown.StopAsync(), _controller?.CheckpointForShutdownAsync() ?? Task.CompletedTask)).Wait(TimeSpan.FromSeconds(3)); } catch { }
        };
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            try { var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaCapture"); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "last-error.log"), args.Exception.ToString()); if (!CaptureShutdown.IsEnding) new ToastWindow("予期しないエラー", args.Exception.Message, false, 7000).ShowNearTaskbar(); } catch { if (!CaptureShutdown.IsEnding) Shutdown(1); }
        };

        var arguments = e.Args;
        if (arguments.Length >= 2 && arguments[0] == "--settings-save-smoke-test")
        {
            try { await DashboardChecks.SettingsSaveAsync(arguments[1]); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(arguments[1]); File.WriteAllText(Path.Combine(arguments[1], "failure.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        if (arguments.Length >= 2 && (arguments[0] == "--dashboard-smoke-test" || arguments[0] == "--shutdown-smoke-test"))
        {
            try { if (arguments[0] == "--dashboard-smoke-test") await DashboardChecks.RenderAsync(arguments[1]); else await DashboardChecks.ShutdownAsync(arguments[1]); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(arguments[1]); File.WriteAllText(Path.Combine(arguments[1], "failure.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        if (arguments.Length >= 2 && arguments[0] == "--recording-regression-test")
        {
            try { await RecordingRegressionChecks.RunAsync(arguments[1]); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(arguments[1]); File.WriteAllText(Path.Combine(arguments[1], "failure.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        if (arguments.Length >= 3 && arguments[0] == "--recording-endurance-test")
        {
            try { await RecordingRegressionChecks.RunEnduranceAsync(arguments[1], int.Parse(arguments[2])); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(arguments[1]); File.WriteAllText(Path.Combine(arguments[1], "failure.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        LocalizationService.SetLanguage(new SettingsService().Load().Language);
        if (arguments.Contains("--package-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunPackageSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--finish-uninstall", StringComparer.OrdinalIgnoreCase)) { var index = Array.FindIndex(arguments, x => x.Equals("--finish-uninstall", StringComparison.OrdinalIgnoreCase)); ShowInstaller(InstallerMode.FinishUninstall, index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null); return; }
        if (arguments.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)) { ShowInstaller(InstallerMode.UninstallConfirm); return; }
        if (arguments.Contains("--install", StringComparer.OrdinalIgnoreCase) || InstallerService.IsSetupExecutableName(Environment.ProcessPath)) { ShowInstaller(InstallerMode.Install); return; }
        if (arguments.Contains("--engine-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunEngineSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--child-job-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunChildJobSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--resolution-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunResolutionSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--audio-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunAudioSmokeTestAsync(false) ? 0 : 1); return; }
        if (arguments.Contains("--microphone-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunMicrophoneSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--audio-level-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(RunAudioLevelSmokeTest() ? 0 : 1); return; }
        if (arguments.Contains("--process-audio-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunAudioSmokeTestAsync(true) ? 0 : 1); return; }
        if (arguments.Contains("--recording-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunRecordingSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--display-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunDisplaySmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--display-reconnect-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunDisplayReconnectSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--application-capture-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunApplicationCaptureSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--fullscreen-application-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunFullscreenApplicationCaptureSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--application-lifecycle-target", StringComparer.OrdinalIgnoreCase)) { await RunApplicationLifecycleTargetAsync(); Shutdown(0); return; }
        if (arguments.Contains("--window-lifecycle-target", StringComparer.OrdinalIgnoreCase)) { await RunApplicationLifecycleTargetAsync(true); Shutdown(0); return; }
        if (arguments.Contains("--window-application-lifecycle-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunApplicationLifecycleSmokeTestAsync(true) ? 0 : 1); return; }
        if (arguments.Contains("--application-lifecycle-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunApplicationLifecycleSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--external-application-lifecycle-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunExternalApplicationLifecycleSmokeTestAsync(arguments) ? 0 : 1); return; }
        if (arguments.Contains("--external-display-lifecycle-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunExternalDisplayLifecycleSmokeTestAsync(arguments) ? 0 : 1); return; }
        if (arguments.Contains("--clip-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(await RunClipSmokeTestAsync() ? 0 : 1); return; }
        if (arguments.Contains("--tray-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(RunTraySmokeTest() ? 0 : 1); return; }
        if (arguments.Contains("--tray-icon-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(TrayIconService.ValidateRenderedIcons() ? 0 : 1); return; }
        if (arguments.Contains("--settings-clip-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(RunSettingsClipSmokeTest() ? 0 : 1); return; }
        if (arguments.Contains("--localization-smoke-test", StringComparer.OrdinalIgnoreCase)) { Shutdown(RunLocalizationSmokeTest() ? 0 : 1); return; }
        if (TryShowPreview(arguments)) return;

        _singleInstance = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            try { using var show = EventWaitHandle.OpenExisting(ShowEventName); show.Set(); } catch { }
            Shutdown(); return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showCancellation = new CancellationTokenSource();
        _ = Task.Run(() => WatchShowEvent(_showCancellation.Token));
        _controller = new AppController();
        await _controller.StartAsync();
    }

    private void WatchShowEvent(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try { if (_showEvent?.WaitOne(500) == true && !cancellation.IsCancellationRequested) Dispatcher.BeginInvoke(() => _controller?.ShowPanel()); }
            catch (ObjectDisposedException) { break; }
        }
    }

    private bool TryShowPreview(string[] arguments)
    {
        var settings = new SettingsService().Load();
        var languageArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--preview-language-", StringComparison.OrdinalIgnoreCase));
        if (languageArgument is not null && Enum.TryParse<AppLanguage>(languageArgument["--preview-language-".Length..], true, out var previewLanguage)) settings.Language = previewLanguage;
        LocalizationService.SetLanguage(settings.Language);
        Window? window = null;
        if (arguments.Contains("--preview-panel", StringComparer.OrdinalIgnoreCase)) window = new CapturePanelWindow(settings.Quality) { AutoHide = false };
        else if (arguments.Any(argument => argument.StartsWith("--preview-settings", StringComparison.OrdinalIgnoreCase))) window = new SettingsWindow(settings);
        else if (arguments.Contains("--preview-tray", StringComparer.OrdinalIgnoreCase)) window = new TrayMenuWindow(false, settings.ClipSeconds, settings.ClipBufferEnabled);
        else if (arguments.Contains("--preview-rec", StringComparer.OrdinalIgnoreCase)) window = new RecIndicatorWindow();
        else if (arguments.Contains("--preview-installer", StringComparer.OrdinalIgnoreCase)) window = new InstallerWindow(InstallerMode.Install);
        if (window is null) return false;
        window.Topmost = true;
        MainWindow = window; window.Closed += (_, _) => Shutdown();
        if (window is CapturePanelWindow panel) panel.ShowCentered(); else if (window is TrayMenuWindow tray) tray.ShowNearCursor(); else if (window is RecIndicatorWindow rec) rec.ShowTopLeft(); else window.Show();
        return true;
    }

    private void ShowInstaller(InstallerMode mode, string? target = null) { var window = new InstallerWindow(mode, target); MainWindow = window; window.Closed += (_, _) => Shutdown(); window.Show(); }

    private static async Task<bool> RunEngineSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-Smoke-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(directory); var settings = new AppSettings { CaptureArea = CaptureArea.AllDisplays, Quality = VideoQuality.Compact, CaptureCursor = false }; var path = Path.Combine(directory, "smoke.mkv"); var process = await new FfmpegService().StartScreenCaptureAsync(settings, path, false); await Task.Delay(1800); await process.StopAsync(); process.Dispose(); return File.Exists(path) && new FileInfo(path).Length > 4096; }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    private static async Task<bool> RunChildJobSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-ChildJobSmoke-" + Guid.NewGuid().ToString("N"));
        IManagedCaptureProcess? capture = null;
        var processId = 0;
        try
        {
            Directory.CreateDirectory(directory);
            var settings = new AppSettings { CaptureArea = CaptureArea.PrimaryDisplay, Quality = VideoQuality.Compact, CaptureCursor = false };
            capture = await new FfmpegService().StartScreenCaptureAsync(settings, Path.Combine(directory, "child.mkv"), false);
            processId = capture switch
            {
                ManagedFfmpegProcess managed => managed.Process.Id,
                ResilientDisplayCaptureProcess resilient => resilient.CurrentProcessId ?? 0,
                _ => 0
            };
            if (processId == 0) return false;
            capture.Dispose();
            capture = null;
            await Task.Delay(900);
            try
            {
                using var remaining = System.Diagnostics.Process.GetProcessById(processId);
                if (!remaining.HasExited) { try { remaining.Kill(true); } catch { } return false; }
                return true;
            }
            catch (ArgumentException) { return true; }
        }
        catch { return false; }
        finally
        {
            capture?.Dispose();
            if (processId != 0)
            {
                try { using var remaining = System.Diagnostics.Process.GetProcessById(processId); if (!remaining.HasExited) remaining.Kill(true); } catch { }
            }
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static async Task<bool> RunResolutionSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-ResolutionSmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var settings = new AppSettings { CaptureArea = CaptureArea.PrimaryDisplay, Resolution = VideoResolution.P720, Quality = VideoQuality.Compact, CaptureCursor = false };
            var path = Path.Combine(directory, "720p.mkv");
            var process = await new FfmpegService().StartScreenCaptureAsync(settings, path, false);
            await Task.Delay(1800); await process.StopAsync(); process.Dispose();
            return File.Exists(path) && new FileInfo(path).Length > 4096;
        }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    private static async Task<bool> RunAudioSmokeTestAsync(bool processAudio)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-AudioSmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var settings = new AppSettings
            {
                AudioCaptureMode = processAudio ? AudioCaptureMode.SelectedApplications : AudioCaptureMode.SystemAudio,
                SelectedApplications = processAudio ? new List<string> { "explorer" } : new List<string>()
            };
            var tracks = await AudioCaptureService.StartFileTracksAsync(settings, directory, "test");
            await Task.Delay(1400);
            foreach (var track in tracks) await track.StopAsync();
            return tracks.Count > 0 && tracks.All(track => File.Exists(track.FilePath) && new FileInfo(track.FilePath).Length > 1000);
        }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    private static async Task<bool> RunMicrophoneSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-MicrophoneSmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var microphone = AudioCaptureService.ListMicrophones().FirstOrDefault();
            if (microphone is null) return false;
            Directory.CreateDirectory(directory);
            var settings = new AppSettings { AudioCaptureMode = AudioCaptureMode.SystemAudio, MicrophoneDeviceId = microphone.Id };
            var tracks = await AudioCaptureService.StartFileTracksAsync(settings, directory, "test");
            await Task.Delay(1600);
            var microphoneChannels = tracks.FirstOrDefault(track => track.Label == "Microphone")?.WaveFormat.Channels ?? 0;
            var capturedAudio = tracks.Select(track => new AudioTrackFile(track.Label, track.FilePath, track.Label == "Microphone", track.WaveFormat.Channels)).ToArray();
            foreach (var track in tracks) await track.StopAsync();
            var microphoneTrack = tracks.FirstOrDefault(track => track.Label == "Microphone");
            if (microphoneTrack is null || !File.Exists(microphoneTrack.FilePath) || new FileInfo(microphoneTrack.FilePath).Length <= 1000) return false;
            var video = Path.Combine(directory, "video.mkv");
            var ffmpeg = new FfmpegService();
            await ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=30", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-an", "-f", "matroska", video });
            settings.AudioOutputMode = AudioOutputMode.SplitPerApplication;
            var finalizer = new RecordingFinalizer(ffmpeg);
            await finalizer.FinalizeAsync(video, new[] { new AudioTrackFile("Microphone", microphoneTrack.FilePath, true, microphoneChannels) }, settings, directory, "centered");
            var centered = Path.Combine(directory, "centered", "Microphone.wav");
            if (!File.Exists(centered)) return false;
            using (var reader = new NAudio.Wave.WaveFileReader(centered)) if (reader.WaveFormat.Channels != 1) return false;
            settings.AudioOutputMode = AudioOutputMode.SplitMixed;
            await finalizer.FinalizeAsync(video, capturedAudio, settings, directory, "mixed");
            if (!File.Exists(Path.Combine(directory, "mixed", "MixedAudio.wav")) || !File.Exists(Path.Combine(directory, "mixed", "Video.mp4"))) return false;
            settings.ClipSeconds = 30;
            var ringDirectory = Path.Combine(directory, "ring");
            var ringTracks = await AudioCaptureService.StartRingTracksAsync(settings, ringDirectory);
            await Task.Delay(1200);
            var microphoneRing = ringTracks.FirstOrDefault(track => track.Label == "Microphone");
            var snapshot = microphoneRing?.SaveSnapshot(directory, "clip", 30);
            foreach (var track in ringTracks) track.Dispose();
            return snapshot is not null && File.Exists(snapshot.Path) && new FileInfo(snapshot.Path).Length > 1000;
        }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    private static bool RunAudioLevelSmokeTest()
    {
        try
        {
            using var monitor = new AudioLevelMonitor();
            var system = monitor.Read(AudioCaptureMode.SystemAudio, Array.Empty<string>(), string.Empty);
            var selected = monitor.Read(AudioCaptureMode.SelectedApplications, new[] { System.Diagnostics.Process.GetCurrentProcess().ProcessName }, string.Empty);
            var microphone = AudioCaptureService.ListMicrophones().FirstOrDefault();
            var withMicrophone = monitor.Read(AudioCaptureMode.SystemAudio, Array.Empty<string>(), microphone?.Id ?? string.Empty);
            var values = new[] { system.Audio, system.Microphone, selected.Audio, selected.Microphone, withMicrophone.Audio, withMicrophone.Microphone };
            var window = new SettingsWindow(new AppSettings());
            var hasCustomMeters = window.AudioLevelBar.Style is not null && window.MicrophoneLevelBar.Style is not null;
            window.Close();
            return hasCustomMeters && values.All(value => float.IsFinite(value) && value >= 0f && value <= 1f);
        }
        catch { return false; }
    }

    private static async Task<bool> RunRecordingSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-RecordingSmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings { OutputDirectory = directory, CaptureArea = CaptureArea.PrimaryDisplay, Quality = VideoQuality.Compact, AudioCaptureMode = AudioCaptureMode.SystemAudio, AudioOutputMode = AudioOutputMode.EmbedInVideo };
            using var service = new RecordingService();
            await service.StartAsync(settings); await Task.Delay(2200); var output = await service.StopAsync();
            return output is not null && File.Exists(output) && new FileInfo(output).Length > 4096;
        }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    private static async Task<bool> RunDisplaySmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-DisplaySmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var screen = System.Windows.Forms.Screen.AllScreens.LastOrDefault();
            if (screen is null) return false;
            Directory.CreateDirectory(directory);
            var settings = new AppSettings { CaptureArea = CaptureArea.SpecificDisplay, CaptureDisplayDeviceName = screen.DeviceName, Quality = VideoQuality.Compact, CaptureCursor = false };
            var path = Path.Combine(directory, "display.mkv");
            var process = await new FfmpegService().StartScreenCaptureAsync(settings, path, false);
            await Task.Delay(1800); await process.StopAsync(); process.Dispose();
            return File.Exists(path) && new FileInfo(path).Length > 4096;
        }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    private static async Task<bool> RunDisplayReconnectSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-DisplayReconnectSmoke-" + Guid.NewGuid().ToString("N"));
        IManagedCaptureProcess? process = null;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "display-reconnected.mkv");
            var settings = new AppSettings { CaptureArea = CaptureArea.PrimaryDisplay, Resolution = VideoResolution.P720, Quality = VideoQuality.Compact, CaptureCursor = false };
            process = await new FfmpegService().StartScreenCaptureAsync(settings, path, false);
            if (process is not ResilientDisplayCaptureProcess resilient) return false;
            await Task.Delay(1600);
            await resilient.RestartForTestAsync();
            await Task.Delay(1600);
            await process.StopAsync();
            process.Dispose();
            process = null;
            if (!File.Exists(path) || new FileInfo(path).Length <= 4096) return false;
            var frame = Path.Combine(directory, "last.png");
            await new FfmpegService().RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-sseof", "-0.2", "-i", path, "-frames:v", "1", frame });
            return File.Exists(frame) && new FileInfo(frame).Length > 1024;
        }
        catch { return false; }
        finally
        {
            process?.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static async Task<bool> RunApplicationCaptureSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-ApplicationSmoke-" + Guid.NewGuid().ToString("N"));
        Window? target = null;
        Window? cover = null;
        var passed = false;
        try
        {
            Directory.CreateDirectory(directory);
            target = new Window { Title = "LumaCapture application capture test", Width = 640, Height = 360, Left = 120, Top = 120, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 70, 110)), WindowStartupLocation = WindowStartupLocation.Manual };
            Application.Current.MainWindow = target;
            target.Show();
            var targetHandle = new System.Windows.Interop.WindowInteropHelper(target).Handle;
            if (!ProcessCatalog.IsSelectableWindow(targetHandle)) return false;
            var activeSelection = new AppSettings { CaptureArea = CaptureArea.SpecificApplication, CaptureApplicationProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName };
            if (ApplicationSelectionPolicy.ClearIfUnavailable(activeSelection)) return false;
            target.WindowState = WindowState.Minimized;
            await Task.Delay(250);
            if (ProcessCatalog.IsSelectableWindow(targetHandle)) return false;
            if (ApplicationSelectionPolicy.ClearIfUnavailable(activeSelection)) return false;
            target.WindowState = WindowState.Normal;
            await Task.Delay(250);
            var missingSelection = new AppSettings { CaptureArea = CaptureArea.SpecificApplication, CaptureApplicationProcessName = "LumaCapture_Missing_Process_Test" };
            if (!ApplicationSelectionPolicy.ClearIfUnavailable(missingSelection) || missingSelection.CaptureArea != CaptureArea.PrimaryDisplay || !string.IsNullOrEmpty(missingSelection.CaptureApplicationProcessName)) return false;
            cover = new Window { Title = "LumaCapture overlap test", Width = 260, Height = 180, Left = 280, Top = 200, Topmost = true, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 35, 55)), WindowStartupLocation = WindowStartupLocation.Manual };
            cover.Owner = target;
            cover.Show();
            await Task.Delay(350);
            var settings = new AppSettings { CaptureArea = CaptureArea.SpecificApplication, CaptureApplicationProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName, Resolution = VideoResolution.P720, Quality = VideoQuality.Compact, CaptureCursor = false };
            var resolved = CaptureTargetResolver.ResolveApplicationWindow(settings.CaptureApplicationProcessName);
            if (resolved.Bounds.Width < 500 || resolved.Bounds.Height < 250 || resolved.Handle == IntPtr.Zero) return false;
            var path = Path.Combine(directory, "application.mkv");
            var ffmpeg = new FfmpegService();
            var process = await ffmpeg.StartScreenCaptureAsync(settings, path, false);
            var sizeWarningRaised = 0;
            if (process is FollowingWindowCaptureProcess applicationCapture)
                applicationCapture.Warning += (_, _) => Interlocked.Increment(ref sizeWarningRaised);
            await Task.Delay(9000); // Static WGC source must not be mistaken for a stall.
            target.WindowState = WindowState.Minimized;
            await Task.Delay(900);
            target.WindowState = WindowState.Normal;
            target.Width = 430; target.Height = 290; target.Left = 540; target.Top = 260;
            target.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 120, 65));
            cover.Left = 600; cover.Top = 310;
            await Task.Delay(2200);
            await process.StopAsync(); process.Dispose();
            if (!File.Exists(path) || new FileInfo(path).Length <= 1500) return false;

            var frame = Path.Combine(directory, "last.png");
            await ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-sseof", "-0.4", "-i", path, "-frames:v", "1", frame });
            using var bitmap = new System.Drawing.Bitmap(frame);
            var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            File.WriteAllText(Path.Combine(directory, "assertions.txt"), $"Warnings={sizeWarningRaised}, Center={center}");
            passed = Volatile.Read(ref sizeWarningRaised) == 1 && Math.Abs(center.R - 40) < 25 && Math.Abs(center.G - 120) < 25 && Math.Abs(center.B - 65) < 25;
            return passed;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString()); return false; }
        finally { cover?.Close(); target?.Close(); if (passed) { try { Directory.Delete(directory, true); } catch { } } }
    }

    private static async Task<bool> RunFullscreenApplicationCaptureSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-FullscreenApplicationSmoke-" + Guid.NewGuid().ToString("N"));
        Window? target = null;
        var passed = false;
        try
        {
            Directory.CreateDirectory(directory);
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen is null) return false;
            var expected = System.Windows.Media.Color.FromRgb(18, 106, 76);
            target = new Window
            {
                Title = "LumaCapture fullscreen application capture test",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowState = WindowState.Maximized,
                Topmost = true,
                Background = new System.Windows.Media.SolidColorBrush(expected),
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = screen.WorkingArea.Left,
                Top = screen.WorkingArea.Top
            };
            Application.Current.MainWindow = target;
            target.Show();
            await Task.Delay(500);
            var settings = new AppSettings { CaptureArea = CaptureArea.SpecificApplication, CaptureApplicationProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName, Resolution = VideoResolution.P720, Quality = VideoQuality.Compact, CaptureCursor = false };
            var resolved = CaptureTargetResolver.ResolveApplicationWindow(settings.CaptureApplicationProcessName);
            if (!FollowingWindowCaptureProcess.IsCompleteFullscreen(resolved.Bounds, screen.Bounds)) return false;

            var path = Path.Combine(directory, "fullscreen-application.mkv");
            var ffmpeg = new FfmpegService();
            var process = await ffmpeg.StartScreenCaptureAsync(settings, path, false);
            await Task.Delay(1500);
            target.WindowState = WindowState.Normal;
            target.Width = 760;
            target.Height = 430;
            var windowedExpected = System.Windows.Media.Color.FromRgb(70, 45, 150);
            target.Background = new System.Windows.Media.SolidColorBrush(windowedExpected);
            target.Left = screen.WorkingArea.Left + 180;
            target.Top = screen.WorkingArea.Top + 140;
            target.Activate();
            await Task.Delay(4500);
            await process.StopAsync();
            process.Dispose();
            if (!File.Exists(path) || new FileInfo(path).Length <= 4096) return false;

            var fullscreenFrame = Path.Combine(directory, "fullscreen.png");
            var windowedFrame = Path.Combine(directory, "windowed.png");
            await ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-ss", "0.7", "-i", path, "-frames:v", "1", fullscreenFrame });
            await ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-sseof", "-1.0", "-i", path, "-frames:v", "1", windowedFrame });
            using var fullscreenBitmap = new System.Drawing.Bitmap(fullscreenFrame);
            using var windowedBitmap = new System.Drawing.Bitmap(windowedFrame);
            var fullscreenCenter = fullscreenBitmap.GetPixel(fullscreenBitmap.Width / 2, fullscreenBitmap.Height / 2);
            var windowedCenter = windowedBitmap.GetPixel(windowedBitmap.Width / 2, windowedBitmap.Height / 2);
            passed = IsNear(fullscreenCenter, expected) && IsNear(windowedCenter, windowedExpected);
            return passed;
        }
        catch { return false; }
        finally { target?.Close(); if (passed) { try { Directory.Delete(directory, true); } catch { } } }
    }

    private static bool IsNear(System.Drawing.Color actual, System.Windows.Media.Color expected) =>
        Math.Abs(actual.R - expected.R) < 40 && Math.Abs(actual.G - expected.G) < 40 && Math.Abs(actual.B - expected.B) < 40;

    private static async Task RunApplicationLifecycleTargetAsync(bool windowed = false)
    {
        var expected = System.Windows.Media.Color.FromRgb(74, 32, 126);
        var target = new Window
        {
            Title = "LumaCapture lifecycle target",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = windowed ? WindowState.Normal : WindowState.Maximized,
            Width = 640, Height = 360,
            Topmost = true,
            Background = new System.Windows.Media.SolidColorBrush(expected)
        };
        Application.Current.MainWindow = target;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        target.Closed += (_, _) => closed.TrySetResult();
        target.Show();
        // The parent requests closure after capture has really started. A fixed
        // short lifetime was racing GPU/encoder initialization on busy machines.
        if (await Task.WhenAny(closed.Task, Task.Delay(20000)) != closed.Task) target.Close();
    }

    private static async Task<bool> RunApplicationLifecycleSmokeTestAsync(bool windowed = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-ApplicationLifecycleSmoke-" + Guid.NewGuid().ToString("N"));
        System.Diagnostics.Process? targetProcess = null;
        IManagedCaptureProcess? capture = null;
        try
        {
            Directory.CreateDirectory(directory);
            var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            info.ArgumentList.Add(windowed ? "--window-lifecycle-target" : "--application-lifecycle-target");
            targetProcess = System.Diagnostics.Process.Start(info);
            if (targetProcess is null) return false;
            var ready = false;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(100);
                targetProcess.Refresh();
                if (!targetProcess.HasExited && targetProcess.MainWindowHandle != IntPtr.Zero) { ready = true; break; }
            }
            if (!ready) return false;

            var path = Path.Combine(directory, "lifecycle.mkv");
            var settings = new AppSettings { CaptureArea = CaptureArea.SpecificApplication, CaptureApplicationProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName, Resolution = VideoResolution.P720, Quality = VideoQuality.Compact, CaptureCursor = false };
            capture = await new FfmpegService().StartScreenCaptureAsync(settings, path, false);
            if (capture is not FollowingWindowCaptureProcess applicationCapture) return false;
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            applicationCapture.TargetEnded += (_, _) => ended.TrySetResult();
            await Task.Delay(1500);
            if (!targetProcess.CloseMainWindow()) throw new Exception("Could not close test capture target");
            var eventResult = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            await capture.StopAsync();
            capture.Dispose();
            capture = null;
            if (eventResult != ended.Task || !File.Exists(path) || new FileInfo(path).Length <= 1024) return false;

            var frame = Path.Combine(directory, "lifecycle.png");
            var ffmpeg = new FfmpegService();
            await ffmpeg.RunAsync(new[] { "-hide_banner", "-y", "-loglevel", "warning", "-ss", "0.7", "-i", path, "-frames:v", "1", frame });
            using var bitmap = new System.Drawing.Bitmap(frame);
            var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            return IsNear(center, System.Windows.Media.Color.FromRgb(74, 32, 126));
        }
        catch { return false; }
        finally
        {
            capture?.Dispose();
            if (targetProcess is not null)
            {
                try { if (!targetProcess.HasExited) targetProcess.Kill(true); } catch { }
                targetProcess.Dispose();
            }
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static async Task<bool> RunExternalApplicationLifecycleSmokeTestAsync(string[] arguments)
    {
        var index = Array.FindIndex(arguments, value => value.Equals("--external-application-lifecycle-smoke-test", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 2 >= arguments.Length) return false;
        var processName = arguments[index + 1];
        var outputPath = Path.GetFullPath(arguments[index + 2]);
        IManagedCaptureProcess? capture = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var settings = new AppSettings { CaptureArea = CaptureArea.SpecificApplication, CaptureApplicationProcessName = processName, Resolution = VideoResolution.P720, Quality = VideoQuality.Compact, CaptureCursor = false };
            capture = await new FfmpegService().StartScreenCaptureAsync(settings, outputPath, false);
            if (capture is not FollowingWindowCaptureProcess applicationCapture) return false;
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            applicationCapture.TargetEnded += (_, _) => ended.TrySetResult();
            var finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(25)));
            await capture.StopAsync();
            capture.Dispose();
            capture = null;
            return finished == ended.Task && File.Exists(outputPath) && new FileInfo(outputPath).Length > 4096;
        }
        catch { return false; }
        finally { capture?.Dispose(); }
    }

    private static async Task<bool> RunExternalDisplayLifecycleSmokeTestAsync(string[] arguments)
    {
        var index = Array.FindIndex(arguments, value => value.Equals("--external-display-lifecycle-smoke-test", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 2 >= arguments.Length || !int.TryParse(arguments[index + 2], out var seconds)) return false;
        var outputPath = Path.GetFullPath(arguments[index + 1]);
        IManagedCaptureProcess? capture = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var settings = new AppSettings { CaptureArea = CaptureArea.PrimaryDisplay, Resolution = VideoResolution.Original, Quality = VideoQuality.Compact, CaptureCursor = false };
            capture = await new FfmpegService().StartScreenCaptureAsync(settings, outputPath, false);
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60)));
            await capture.StopAsync();
            capture.Dispose();
            capture = null;
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 4096;
        }
        catch { return false; }
        finally { capture?.Dispose(); }
    }

    private static async Task<bool> RunClipSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-ClipSmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = Path.Combine(directory, "custom-cache");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, ".lumacapture-clip-cache"), "simulated crash");
            File.WriteAllBytes(Path.Combine(cache, "segment_crash_000001.mkv"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(cache, "audio_crash.pcm"), new byte[2048]);
            var staleJob = Path.Combine(cache, "job_crash"); Directory.CreateDirectory(staleJob); File.WriteAllText(Path.Combine(staleJob, "partial.tmp"), "partial");
            var sentinel = Path.Combine(cache, "keep-user-file.txt"); File.WriteAllText(sentinel, "keep");
            var settings = new AppSettings { OutputDirectory = directory, ClipCacheDirectory = cache, CaptureArea = CaptureArea.PrimaryDisplay, Quality = VideoQuality.Compact, AudioCaptureMode = AudioCaptureMode.SystemAudio, AudioOutputMode = AudioOutputMode.EmbedInVideo, ClipBufferEnabled = false, ClipSeconds = 30 };
            using (var recovery = new ClipBufferService()) { await recovery.StartAsync(settings); await recovery.StopAsync(); }
            if (ClipBufferService.HasManagedCacheData(cache) || !File.Exists(sentinel)) return false;
            settings.ClipBufferEnabled = true;
            using var service = new ClipBufferService();
            await service.StartAsync(settings); await Task.Delay(5200); var output = await service.SaveClipAsync(); await service.StopAsync();
            return File.Exists(output) && new FileInfo(output).Length > 4096 && !ClipBufferService.HasManagedCacheData(cache) && File.Exists(sentinel);
        }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    private static bool RunTraySmokeTest()
    {
        try
        {
            var window = new TrayMenuWindow(false, 60, true);
            window.Show();
            window.RequestClose();
            window.RequestClose();
            return !window.IsVisible;
        }
        catch { return false; }
    }

    private static bool RunSettingsClipSmokeTest()
    {
        try
        {
            foreach (var seconds in new[] { 30, 60, 120, 300, 600 })
            {
                var window = new SettingsWindow(new AppSettings { ClipSeconds = seconds });
                var value = window.FindName("ClipValueText") as System.Windows.Controls.TextBlock;
                var isCorrect = value?.Text == $"{seconds} 秒" && window.ClipDisplayCombo.SelectedItem is System.Windows.Controls.ComboBoxItem && !string.IsNullOrWhiteSpace(window.ClipCacheDirectoryBox.Text);
                window.Close();
                if (!isCorrect) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static bool RunLocalizationSmokeTest()
    {
        try
        {
            var expected = new Dictionary<AppLanguage, string>
            {
                [AppLanguage.Japanese] = "設定", [AppLanguage.English] = "Settings", [AppLanguage.Korean] = "설정",
                [AppLanguage.ChineseSimplified] = "设置", [AppLanguage.ChineseTraditional] = "設定"
            };
            foreach (var pair in expected)
            {
                LocalizationService.SetLanguage(pair.Key);
                if (LocalizationService.T("設定") != pair.Value) return false;
                var window = new SettingsWindow(new AppSettings { Language = pair.Key, ClipSeconds = 60 });
                var firstHeader = (window.SettingsTabs.Items[0] as System.Windows.Controls.TabItem)?.Header?.ToString();
                var expectedHeader = pair.Key switch { AppLanguage.English => "Save & Record", AppLanguage.Korean => "저장 및 녹화", AppLanguage.ChineseSimplified => "保存和录制", AppLanguage.ChineseTraditional => "儲存與錄製", _ => "保存・録画" };
                var expectedAlways = pair.Key switch { AppLanguage.English => "Always show the recording controls", AppLanguage.Korean => "녹화 버튼 항상 표시", AppLanguage.ChineseSimplified => "始终显示录制按钮", AppLanguage.ChineseTraditional => "永遠顯示錄製按鈕", _ => "録画ボタンを常に表示する" };
                var valid = firstHeader == expectedHeader && window.AlwaysShowPanelCheck.Content?.ToString() == expectedAlways && !string.IsNullOrWhiteSpace(window.ClipValueText.Text);
                window.Close();
                if (!valid) return false;
            }
            LocalizationService.SetLanguage(AppLanguage.Japanese);
            return true;
        }
        catch { return false; }
    }

    private static async Task<bool> RunPackageSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaCapture-PackageSmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var tracing = new System.Diagnostics.Tracing.EventSource("LumaCapture.PackageValidation");
            var installer = new InstallerWindow(InstallerMode.Install);
            foreach (var language in Enum.GetValues<AppLanguage>()) installer.SelectLanguageForTest(language);
            installer.Close();
            Directory.CreateDirectory(directory);
            var ffmpeg = Path.Combine(directory, "ffmpeg.exe");
            if (!await Task.Run(() => FfmpegService.ExtractEmbeddedExecutable(ffmpeg))) return false;
            if (new FileInfo(ffmpeg).Length < 10 * 1024 * 1024) return false;
            var info = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-hide_banner"); info.ArgumentList.Add("-filters");
            using var process = System.Diagnostics.Process.Start(info);
            if (process is null) return false;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && output.Contains("gfxcapture", StringComparison.OrdinalIgnoreCase) && output.Contains("ddagrab", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showCancellation?.Cancel(); _showEvent?.Set(); _showEvent?.Dispose(); _showCancellation?.Dispose(); _controller?.Dispose(); _singleInstance?.Dispose(); base.OnExit(e);
    }
}
