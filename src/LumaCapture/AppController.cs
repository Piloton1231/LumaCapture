using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using LumaCapture.Infrastructure;
using LumaCapture.Models;
using LumaCapture.Windows;

namespace LumaCapture;

public sealed class AppController : IDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly RecordingService _recording = new();
    private readonly ClipBufferService _clips = new();
    private readonly HotkeyService _hotkeys;
    private readonly TrayIconService _tray;
    private readonly StorageMonitorService _storage;
    private readonly CaptureOverlayService _overlays = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _applicationSelectionTimer;
    private AppSettings _settings;
    private CapturePanelWindow? _panel;
    private RecIndicatorWindow? _indicator;
    private TrayMenuWindow? _trayMenu;
    private bool _disposed;
    private bool _closing;
    private bool _recordBusy;
    private int _autoStopping;
    private int _targetExitStopping;
    private int _missingApplicationChecks;

    public AppController()
    {
        _settings = _settingsService.Load();
        LocalizationService.SetLanguage(_settings.Language);
        _tray = new TrayIconService();
        _hotkeys = new HotkeyService(Application.Current.Dispatcher);
        _storage = new StorageMonitorService(() => _settings.Clone(), () => _recording.IsRecording);
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick += (_, _) => UpdateRecordingUi();
        _applicationSelectionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _applicationSelectionTimer.Tick += (_, _) => CheckApplicationSelection();
        _hotkeys.CapturePressed += (_, _) => Application.Current.Dispatcher.BeginInvoke(ShowPanel);
        _hotkeys.ClipPressed += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => _ = SaveClipAsync());
        _tray.LeftClicked += (_, _) => Application.Current.Dispatcher.BeginInvoke(ShowPanel);
        _tray.RightClicked += (_, _) => Application.Current.Dispatcher.BeginInvoke(ShowTrayMenu);
        _recording.StateChanged += (_, recording) => Application.Current.Dispatcher.BeginInvoke(() => OnRecordingStateChanged(recording));
        _recording.Warning += (_, message) => Application.Current.Dispatcher.BeginInvoke(() => ShowToast("音声の確認", message, false, 5200));
        _recording.CaptureWarning += (_, message) => Application.Current.Dispatcher.BeginInvoke(() => ShowToast("録画対象のサイズ変更", message, false, 6200));
        _recording.CaptureTargetEnded += (_, message) => Application.Current.Dispatcher.BeginInvoke(() => _ = StopForTargetExitAsync(message));
        _clips.Warning += (_, message) => Application.Current.Dispatcher.BeginInvoke(() => ShowToast("クリップの確認", message, false, 5200));
        _storage.LowSpace += (_, free) => Application.Current.Dispatcher.BeginInvoke(() => ShowToast("空き容量が少なくなっています", LocalizationService.StorageRemaining(free), false, 5200));
        _storage.AutoStopRequired += (_, free) => Application.Current.Dispatcher.BeginInvoke(() => _ = AutoStopAsync(free));
    }

    public async Task StartAsync()
    {
        ConfigureHotkeys();
        if (!InstallerService.IsSetupExecutableName(Environment.ProcessPath) && Environment.ProcessPath is { } executable)
            StartupService.Apply(_settings.LaunchAtStartup, executable);
        await StartClipBufferSafeAsync();
        _applicationSelectionTimer.Start();
        if (_settings.AlwaysShowCapturePanel) ShowPanel();
    }

    public void ShowPanel()
    {
        if (_closing || CaptureShutdown.IsEnding) return;
        CloseTrayMenu();
        if (_panel is null)
        {
            _panel = new CapturePanelWindow(_settings.Quality) { AutoHide = !_settings.AlwaysShowCapturePanel };
            _panel.RecordToggleRequested += (_, _) => _ = ToggleRecordingAsync();
            _panel.SettingsRequested += (_, _) => ShowSettings();
            _panel.AppSelectionRequested += (_, _) => ShowApplicationPicker();
            _panel.PositionChangedByUser += (_, _) => SavePanelPosition();
            _panel.FolderRequested += (_, _) => OpenOutputFolder();
            _panel.AudioSettingsRequested += (_, _) => ShowSettings(1);
            _panel.ClipRequested += (_, _) => _ = SaveClipAsync();
            _panel.ResolutionChanged += (_, resolution) => { if (_recording.IsRecording || _recordBusy) return; _settings.Resolution = resolution; _settingsService.Save(_settings); };
            _panel.TargetChanged += (_, target) =>
            {
                if (_recording.IsRecording || _recordBusy) return;
                _settings.CaptureArea = target.Area; _settings.CaptureDisplayDeviceName = target.Device;
                if (target.Area != CaptureArea.SpecificApplication) _settings.CaptureApplicationProcessName = string.Empty;
                _settingsService.Save(_settings); _panel.UpdateTarget(CaptureTargetLabel()); UpdateCaptureOverlays();
            };
            _panel.IsVisibleChanged += (_, _) => UpdateCaptureOverlays();
            _panel.QualityChanged += (_, quality) => { if (_recording.IsRecording || _recordBusy) return; _settings.Quality = quality; _settingsService.Save(_settings); };
            _panel.Closed += (_, _) => { _panel = null; _overlays.Hide(); };
        }
        _panel.AutoHide = !_settings.AlwaysShowCapturePanel;
        _panel.SetQuality(_settings.Quality);
        _panel.UpdateTarget(CaptureTargetLabel());
        _panel.UpdateRecording(_recording.IsRecording, _recording.Elapsed);
        _panel.UpdateConfiguration(_settings, _clips.IsRunning);
        _panel.ShowAtSavedPosition(_settings.CapturePanelLeft, _settings.CapturePanelTop);
        UpdateCaptureOverlays();
    }

    private async Task ToggleRecordingAsync()
    {
        if (_closing || _recordBusy || _recording.IsProcessing) return;
        _recordBusy = true; _panel?.SetBusy(true);
        try { if (_recording.IsRecording) await StopRecordingAsync(); else await StartRecordingAsync(); }
        finally { _recordBusy = false; _panel?.SetBusy(false); UpdateRecordingUi(); _panel?.UpdateConfiguration(_settings, _clips.IsRunning); }
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            StorageMonitorService.EnsureCanStart(_settings);
            _ = CaptureTargetResolver.Resolve(_settings);
            // The instant replay buffer already captures the desktop. Pause it while a
            // normal recording is active so two capture engines never fight over the
            // cursor or consume double the CPU/GPU resources.
            if (_clips.IsRunning) await _clips.StopAsync();
            await _recording.StartAsync(_settings.Clone());
            if (!_settings.AlwaysShowCapturePanel) _panel?.Hide();
        }
        catch (Exception ex)
        {
            ShowToast("録画を開始できません", ex.Message, false, 6000);
            await StartClipBufferSafeAsync();
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            if (!_settings.AlwaysShowCapturePanel) _panel?.Hide();
            ShowToast("録画を保存しています", "動画と音声を仕上げています。", true, 2200);
            var output = await _recording.StopAsync();
            if (output is not null) ShowToast("録画を保存しました", SavedOutputName(output), true, 4200);
        }
        catch (Exception ex) { ShowToast("録画の保存に失敗しました", ex.Message, false, 7000); }
        finally
        {
            await StartClipBufferSafeAsync();
            if (_settings.AlwaysShowCapturePanel) ShowPanel();
        }
    }

    private async Task AutoStopAsync(double free)
    {
        if (Interlocked.Exchange(ref _autoStopping, 1) != 0 || !_recording.IsRecording) return;
        try
        {
            ShowToast("容量保護で録画を停止します", LocalizationService.AutoStopStorage(free), false, 6000);
            await StopRecordingAsync();
        }
        finally { Interlocked.Exchange(ref _autoStopping, 0); }
    }

    private async Task StopForTargetExitAsync(string message)
    {
        if (Interlocked.Exchange(ref _targetExitStopping, 1) != 0 || !_recording.IsRecording) return;
        try
        {
            ShowToast("録画対象のアプリが終了しました", message, false, 5200);
            await StopRecordingAsync();
        }
        finally { Interlocked.Exchange(ref _targetExitStopping, 0); }
    }

    private void OnRecordingStateChanged(bool recording)
    {
        if (_closing || CaptureShutdown.IsEnding) return;
        _tray.SetRecording(recording);
        if (recording)
        {
            _uiTimer.Start();
            if (_settings.ShowRecIndicator) { _indicator = new RecIndicatorWindow(); _indicator.ShowTopLeft(); }
        }
        else
        {
            _uiTimer.Stop(); _indicator?.Close(); _indicator = null;
        }
        UpdateRecordingUi();
        UpdateCaptureOverlays();
    }

    private void UpdateRecordingUi()
    {
        _panel?.UpdateRecording(_recording.IsRecording, _recording.Elapsed);
        _indicator?.Update(_recording.Elapsed);
        if (_panel?.IsVisible == true && !_recording.IsRecording) _overlays.Show(_settings);
    }

    private void UpdateCaptureOverlays()
    {
        if (_panel?.IsVisible == true && !_recording.IsRecording) _overlays.Show(_settings);
        else _overlays.Hide();
    }

    private void SavePanelPosition()
    {
        if (_panel is null) return;
        _settings.CapturePanelLeft = _panel.Left;
        _settings.CapturePanelTop = _panel.Top;
        _settingsService.Save(_settings);
    }

    private string CaptureTargetLabel()
    {
        if (_settings.CaptureArea == CaptureArea.SpecificApplication) return LocalizationService.T("アプリ") + ": " + _settings.CaptureApplicationProcessName;
        if (_settings.CaptureArea == CaptureArea.AllDisplays) return LocalizationService.CurrentLanguage switch { AppLanguage.English => "All displays", AppLanguage.Korean => "모든 화면", AppLanguage.ChineseSimplified => "所有屏幕", AppLanguage.ChineseTraditional => "所有螢幕", _ => "すべての画面" };
        if (_settings.CaptureArea == CaptureArea.SpecificDisplay) return _settings.CaptureDisplayDeviceName;
        return LocalizationService.CurrentLanguage switch { AppLanguage.English => "Main display", AppLanguage.Korean => "주 화면", AppLanguage.ChineseSimplified => "主屏幕", AppLanguage.ChineseTraditional => "主螢幕", _ => "メイン画面" };
    }

    private void ShowApplicationPicker()
    {
        _overlays.Hide();
        var picker = new ApplicationPickerWindow(_settings.CaptureApplicationProcessName);
        if (picker.ShowDialog() == true)
        {
            if (picker.SelectionCleared)
            {
                _settings.CaptureArea = CaptureArea.PrimaryDisplay;
                _settings.CaptureApplicationProcessName = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(picker.SelectedProcessName))
            {
                _settings.CaptureArea = CaptureArea.SpecificApplication;
                _settings.CaptureApplicationProcessName = picker.SelectedProcessName;
            }
            else return;
            ApplicationSelectionPolicy.ClearIfUnavailable(_settings);
            _settingsService.Save(_settings);
            _panel?.UpdateTarget(CaptureTargetLabel());
            _panel?.UpdateConfiguration(_settings, _clips.IsRunning);
            _ = StartClipBufferSafeAsync();
        }
        if (_panel?.IsVisible == true) UpdateCaptureOverlays();
    }

    private async Task SaveClipAsync()
    {
        CloseTrayMenu();
        if (!_settings.ClipBufferEnabled) { ShowToast("クリップは無効です", "設定の「クリップ」から有効にできます。", false); return; }
        if (_recording.IsRecording) { ShowToast("通常録画中です", "直前クリップは録画停止後に自動で再開します。", false, 4800); return; }
        try
        {
            StorageMonitorService.EnsureCanStart(_settings);
            ShowToast("クリップを保存しています", LocalizationService.ClipFinalizing(_settings.ClipSeconds), true, 2200);
            var output = await _clips.SaveClipAsync();
            ShowToast("クリップを保存しました", SavedOutputName(output), true, 4200);
        }
        catch (Exception ex) { ShowToast("クリップを保存できません", ex.Message, false, 6500); }
    }

    private async Task StartClipBufferSafeAsync()
    {
        if (_closing || CaptureShutdown.IsEnding || _recording.IsRecording) return;
        try { await _clips.StartAsync(_settings.Clone()); }
        catch (Exception ex) { ShowToast("クリップ機能を開始できません", ex.Message, false, 6000); }
    }

    private void ShowSettings(int selectedTab = 0)
    {
        if (_closing || _recordBusy) return;
        CloseTrayMenu(); _panel?.Hide(); _hotkeys.Stop();
        var window = new SettingsWindow(_settings.Clone());
        ((System.Windows.Controls.TabControl)window.FindName("SettingsTabs")).SelectedIndex = selectedTab;
        var accepted = window.ShowDialog() == true;
        if (accepted && window.ResultSettings is not null)
        {
            _settings = window.ResultSettings;
            ApplicationSelectionPolicy.ClearIfUnavailable(_settings);
            LocalizationService.SetLanguage(_settings.Language);
            _tray.RefreshText(_recording.IsRecording);
            _settingsService.Save(_settings);
            if (Environment.ProcessPath is { } executable && !InstallerService.IsSetupExecutableName(executable)) StartupService.Apply(_settings.LaunchAtStartup, executable);
            _ = StartClipBufferSafeAsync();
            ShowToast("設定を保存しました", "次の録画から新しい設定を使います。", true);
        }
        if (_panel is not null)
        {
            LocalizationService.Apply(_panel);
            _panel.AutoHide = !_settings.AlwaysShowCapturePanel;
            _panel.SetQuality(_settings.Quality);
            _panel.UpdateTarget(CaptureTargetLabel());
            _panel.UpdateRecording(_recording.IsRecording, _recording.Elapsed);
            _panel.UpdateConfiguration(_settings, _clips.IsRunning);
        }
        ShowPanel();
        ConfigureHotkeys();
    }

    private void ConfigureHotkeys()
    {
        var status = _hotkeys.Configure(_settings);
        if (!status.CaptureRegistered || !status.ClipRegistered)
            ShowToast("ホットキーを登録できません", "別のアプリと重複している可能性があります。設定から変更してください。", false, 6000);
    }

    private void ShowTrayMenu()
    {
        if (_trayMenu is { IsVisible: true }) { CloseTrayMenu(); return; }
        _trayMenu = new TrayMenuWindow(_recording.IsRecording, _settings.ClipSeconds, _settings.ClipBufferEnabled);
        _trayMenu.ShowPanelRequested += (_, _) => ShowPanel(); _trayMenu.RecordRequested += (_, _) => _ = ToggleRecordingAsync(); _trayMenu.ClipRequested += (_, _) => _ = SaveClipAsync(); _trayMenu.OpenFolderRequested += (_, _) => OpenOutputFolder(); _trayMenu.SettingsRequested += (_, _) => ShowSettings(); _trayMenu.ExitRequested += (_, _) => _ = ExitAsync(); _trayMenu.Closed += (_, _) => _trayMenu = null; _trayMenu.ShowNearCursor();
    }

    private void CloseTrayMenu()
    {
        var menu = _trayMenu;
        _trayMenu = null;
        menu?.RequestClose();
    }
    private void OpenOutputFolder() { CloseTrayMenu(); try { Directory.CreateDirectory(_settings.OutputDirectory); Process.Start(new ProcessStartInfo(_settings.OutputDirectory) { UseShellExecute = true }); } catch (Exception ex) { ShowToast("保存先を開けません", ex.Message, false); } }
    private static void ShowToast(string title, string message, bool success, int duration = 3400) { if (!CaptureShutdown.IsEnding && !Application.Current.Dispatcher.HasShutdownStarted) new ToastWindow(LocalizationService.T(title), LocalizationService.T(message), success, duration).ShowNearTaskbar(); }
    private static string SavedOutputName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!string.Equals(fileName, "Video.mp4", StringComparison.OrdinalIgnoreCase)) return fileName;
        return Path.GetFileName(Path.GetDirectoryName(path)) ?? fileName;
    }

    private async Task ExitAsync()
    {
        CloseTrayMenu();
        if (_settings.ClipBufferEnabled && _settings.ConfirmClipOnExit && _clips.IsRunning)
        {
            var prompt = new ClipExitPromptWindow(_settings.ClipSeconds);
            prompt.ShowDialog();
            if (prompt.Choice == ClipExitChoice.Cancel) return;
            if (prompt.Choice == ClipExitChoice.Save)
            {
                try
                {
                    ShowToast("クリップを保存しています", LocalizationService.Seconds(_settings.ClipSeconds), true, 2200);
                    var output = await _clips.SaveClipAsync();
                    ShowToast("クリップを保存しました", SavedOutputName(output), true, 4200);
                }
                catch (Exception ex) { ShowToast("クリップを保存できません", ex.Message, false, 6500); return; }
            }
        }
        _closing = true;
        try { if (_recording.IsRecording) await _recording.StopAsync(); await _clips.StopAsync(); } catch { }
        if (ApplicationSelectionPolicy.ClearIfUnavailable(_settings)) _settingsService.Save(_settings);
        Dispose(); Application.Current.Shutdown();
    }

    private void CheckApplicationSelection()
    {
        if (_settings.CaptureArea != CaptureArea.SpecificApplication || ProcessCatalog.HasApplicationWindow(_settings.CaptureApplicationProcessName))
        {
            _missingApplicationChecks = 0;
            return;
        }
        if (++_missingApplicationChecks < 3) return;
        _missingApplicationChecks = 0;
        if (!ApplicationSelectionPolicy.ClearIfUnavailable(_settings)) return;
        _settingsService.Save(_settings);
        _panel?.UpdateTarget(CaptureTargetLabel());
        UpdateCaptureOverlays();
        ShowToast("アプリの選択を解除しました", "選択したアプリが終了したため、録画対象をメイン画面に戻しました。", false, 5200);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _uiTimer.Stop(); _applicationSelectionTimer.Stop(); _overlays.Dispose(); _panel?.Close(); _indicator?.Close(); _hotkeys.Dispose(); _storage.Dispose(); _clips.Dispose(); _recording.Dispose(); _tray.Dispose();
    }
    internal void BeginSystemShutdown()
    {
        _closing = true;
        _uiTimer.Stop(); _applicationSelectionTimer.Stop(); _hotkeys.Stop(); _storage.Dispose();
    }
    internal Task CheckpointForShutdownAsync() => _recording.CheckpointForShutdownAsync();
}
