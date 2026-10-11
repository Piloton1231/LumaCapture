using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LumaCapture.Infrastructure;
using LumaCapture.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace LumaCapture.Windows;

public partial class SettingsWindow : Window
{
    private static readonly int[] ClipDurations = { 30, 60, 120, 300, 600 };
    private readonly AppSettings _working;
    private readonly AppLanguage _originalLanguage;
    private HotkeyGesture _captureHotkey;
    private HotkeyGesture _clipHotkey;
    private readonly AudioLevelMonitor _audioLevelMonitor = new();
    private readonly DispatcherTimer _audioLevelTimer;
    private double _displayedAudioLevel;
    private double _displayedMicrophoneLevel;
    public AppSettings? ResultSettings { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        _originalLanguage = settings.Language;
        LocalizationService.SetLanguage(settings.Language);
        InitializeComponent();
        LocalizationService.Apply(this);
        _working = settings.Clone(); _captureHotkey = settings.CaptureHotkey.Clone(); _clipHotkey = settings.ClipHotkey.Clone();
        OutputDirectoryBox.Text = settings.OutputDirectory; CursorCheck.IsChecked = settings.CaptureCursor; RecIndicatorCheck.IsChecked = settings.ShowRecIndicator; AlwaysShowPanelCheck.IsChecked = settings.AlwaysShowCapturePanel; StartupCheck.IsChecked = settings.LaunchAtStartup;
        WarningGbBox.Text = settings.LowSpaceWarningGb.ToString("0.##", CultureInfo.InvariantCulture); AutoStopGbBox.Text = settings.AutoStopGb.ToString("0.##", CultureInfo.InvariantCulture);
        PopulateCaptureTargets(settings); SelectByTag(ResolutionCombo, settings.Resolution.ToString()); SelectByTag(QualityCombo, settings.Quality.ToString()); SelectByTag(AudioModeCombo, settings.AudioCaptureMode.ToString()); SelectByTag(AudioOutputCombo, settings.AudioOutputMode.ToString()); PopulateMicrophones(settings.MicrophoneDeviceId);
        PopulateClipDisplays(settings.ClipDisplayDeviceName);
        ClipCacheDirectoryBox.Text = settings.ClipCacheDirectory;
        ClipEnabledCheck.IsChecked = settings.ClipBufferEnabled; ConfirmClipOnExitCheck.IsChecked = settings.ConfirmClipOnExit;
        ClipSecondsSlider.Value = ClosestClipDurationIndex(settings.ClipSeconds);
        UpdateClipValueText(ClipSecondsSlider.Value);
        SelectByTag(LanguageCombo, settings.Language.ToString());
        UpdateHotkeyText(); RefreshProcesses();
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--preview-settings-audio", StringComparer.OrdinalIgnoreCase)) SettingsTabs.SelectedIndex = 1;
        if (arguments.Contains("--preview-settings-clip", StringComparer.OrdinalIgnoreCase)) SettingsTabs.SelectedIndex = 2;
        if (arguments.Contains("--preview-settings-info", StringComparer.OrdinalIgnoreCase)) SettingsTabs.SelectedIndex = 4;
        if (arguments.Contains("--preview-settings-displays", StringComparer.OrdinalIgnoreCase)) Loaded += (_, _) => CaptureAreaCombo.IsDropDownOpen = true;
        if (!arguments.Any(argument => argument.StartsWith("--preview-settings", StringComparison.OrdinalIgnoreCase)))
            SourceInitialized += (_, _) => WindowProtection.ExcludeFromCapture(this);
        _audioLevelTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, UpdateAudioLevels, Dispatcher);
        Closed += (_, _) => { _audioLevelTimer.Stop(); _audioLevelMonitor.Dispose(); };
    }

    private void PopulateCaptureTargets(AppSettings settings)
    {
        CaptureAreaCombo.Items.Clear();
        var primaryLabel = LocalizationService.CurrentLanguage switch { AppLanguage.English => "Main display (recommended, fastest)", AppLanguage.Korean => "주 화면(권장, 가장 빠름)", AppLanguage.ChineseSimplified => "主屏幕（推荐、最快）", AppLanguage.ChineseTraditional => "主螢幕（建議、最快）", _ => "メイン画面（推奨・最も軽い）" };
        var primaryItem = AddCaptureTarget(primaryLabel, CaptureArea.PrimaryDisplay, string.Empty, string.Empty);
        ComboBoxItem? selected = settings.CaptureArea == CaptureArea.PrimaryDisplay ? primaryItem : null;
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var index = 0; index < screens.Length; index++)
        {
            var screen = screens[index];
            var main = screen.Primary ? LocalizationService.CurrentLanguage switch { AppLanguage.English => "  (main)", AppLanguage.Korean => "  (주 화면)", AppLanguage.ChineseSimplified => "  （主屏幕）", AppLanguage.ChineseTraditional => "  （主螢幕）", _ => "  （メイン）" } : string.Empty;
            var screenWord = LocalizationService.CurrentLanguage switch { AppLanguage.English => "Display", AppLanguage.Korean => "화면", AppLanguage.ChineseSimplified => "屏幕", AppLanguage.ChineseTraditional => "螢幕", _ => "画面" };
            var label = $"{screenWord} {index + 1}  ·  {screen.Bounds.Width} × {screen.Bounds.Height}{main}";
            var item = AddCaptureTarget(label, CaptureArea.SpecificDisplay, screen.DeviceName, string.Empty);
            if (settings.CaptureArea == CaptureArea.SpecificDisplay && string.Equals(settings.CaptureDisplayDeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase)) selected = item;
        }
        var allLabel = LocalizationService.CurrentLanguage switch { AppLanguage.English => $"All displays ({screens.Length})", AppLanguage.Korean => $"모든 화면({screens.Length}개)", AppLanguage.ChineseSimplified => $"所有屏幕（{screens.Length}）", AppLanguage.ChineseTraditional => $"所有螢幕（{screens.Length}）", _ => $"すべての画面（{screens.Length}画面）" };
        var allItem = AddCaptureTarget(allLabel, CaptureArea.AllDisplays, string.Empty, string.Empty);
        if (settings.CaptureArea == CaptureArea.AllDisplays) selected = allItem;
        foreach (var process in ProcessCatalog.ListAvailable())
        {
            var item = AddCaptureTarget(LocalizationService.T("アプリ") + "  ·  " + process.DisplayName, CaptureArea.SpecificApplication, string.Empty, process.ProcessName);
            if (settings.CaptureArea == CaptureArea.SpecificApplication && string.Equals(settings.CaptureApplicationProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase)) selected = item;
        }
        CaptureAreaCombo.SelectedItem = selected ?? primaryItem;
    }

    private ComboBoxItem AddCaptureTarget(string label, CaptureArea area, string deviceName, string processName)
    {
        var item = new ComboBoxItem { Content = label, Tag = new CaptureTarget(area, deviceName, processName) };
        CaptureAreaCombo.Items.Add(item);
        return item;
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items) if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal)) { box.SelectedItem = item; return; }
        box.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    private void PopulateClipDisplays(string selectedDeviceName)
    {
        if (ClipDisplayCombo is null) return;
        ClipDisplayCombo.Items.Clear();
        ComboBoxItem? selected = null;
        ComboBoxItem? primary = null;
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var index = 0; index < screens.Length; index++)
        {
            var screen = screens[index];
            var screenWord = LocalizationService.CurrentLanguage switch { AppLanguage.English => "Display", AppLanguage.Korean => "화면", AppLanguage.ChineseSimplified => "屏幕", AppLanguage.ChineseTraditional => "螢幕", _ => "画面" };
            var main = screen.Primary ? LocalizationService.CurrentLanguage switch { AppLanguage.English => "  (main)", AppLanguage.Korean => "  (주 화면)", AppLanguage.ChineseSimplified => "  （主屏幕）", AppLanguage.ChineseTraditional => "  （主螢幕）", _ => "  （メイン）" } : string.Empty;
            var item = new ComboBoxItem { Content = $"{screenWord} {index + 1}  ·  {screen.Bounds.Width} × {screen.Bounds.Height}{main}", Tag = screen.DeviceName };
            ClipDisplayCombo.Items.Add(item);
            if (screen.Primary) primary = item;
            if (string.Equals(screen.DeviceName, selectedDeviceName, StringComparison.OrdinalIgnoreCase)) selected = item;
        }
        ClipDisplayCombo.SelectedItem = selected ?? primary ?? ClipDisplayCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }
    private void AudioModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ApplicationsPanel is not null) ApplicationsPanel.IsEnabled = SelectedTag(AudioModeCombo) == nameof(AudioCaptureMode.SelectedApplications); }
    private void MicrophoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (MicrophoneLevelText is not null && string.IsNullOrWhiteSpace(SelectedTag(MicrophoneCombo))) MicrophoneLevelText.Text = "OFF"; }

    private void UpdateAudioLevels(object? sender, EventArgs e)
    {
        if (!IsVisible || SettingsTabs.SelectedIndex != 1) return;
        var mode = Enum.TryParse<AudioCaptureMode>(SelectedTag(AudioModeCombo), out var selectedMode) ? selectedMode : AudioCaptureMode.SystemAudio;
        var applications = ProcessList.Children.OfType<CheckBox>().Where(box => box.IsChecked == true).Select(box => box.Tag?.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>();
        var microphoneId = SelectedTag(MicrophoneCombo);
        var levels = _audioLevelMonitor.Read(mode, applications, microphoneId);
        _displayedAudioLevel = SmoothLevel(_displayedAudioLevel, levels.Audio * 100d);
        _displayedMicrophoneLevel = SmoothLevel(_displayedMicrophoneLevel, levels.Microphone * 100d);
        AudioLevelBar.Value = _displayedAudioLevel;
        MicrophoneLevelBar.Value = _displayedMicrophoneLevel;
        AudioLevelText.Text = LocalizationService.T(_displayedAudioLevel >= 1 ? "反応あり" : "待機中");
        MicrophoneLevelText.Text = string.IsNullOrWhiteSpace(microphoneId) ? "OFF" : LocalizationService.T(_displayedMicrophoneLevel >= 1 ? "反応あり" : "待機中");
    }

    private static double SmoothLevel(double current, double next) => next >= current ? next : Math.Max(next, current * 0.72d);
    private static int ClosestClipDurationIndex(int seconds) => Enumerable.Range(0, ClipDurations.Length).OrderBy(index => Math.Abs(ClipDurations[index] - seconds)).First();
    private static int SelectedClipDuration(double sliderValue) => ClipDurations[Math.Clamp((int)Math.Round(sliderValue), 0, ClipDurations.Length - 1)];
    private void ClipSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateClipValueText(e.NewValue);
    private void UpdateClipValueText(double sliderValue) { if (ClipValueText is not null) ClipValueText.Text = LocalizationService.Seconds(SelectedClipDuration(sliderValue)); }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !Enum.TryParse<AppLanguage>(SelectedTag(LanguageCombo), out var language)) return;
        var clipDisplay = SelectedTag(ClipDisplayCombo);
        if (CaptureAreaCombo.SelectedItem is ComboBoxItem { Tag: CaptureTarget currentTarget })
        {
            _working.CaptureArea = currentTarget.Area;
            _working.CaptureDisplayDeviceName = currentTarget.DeviceName;
            _working.CaptureApplicationProcessName = currentTarget.ProcessName;
        }
        LocalizationService.SetLanguage(language);
        LocalizationService.Apply(this);
        _working.Language = language;
        PopulateCaptureTargets(_working);
        PopulateClipDisplays(clipDisplay);
        PopulateMicrophones(SelectedTag(MicrophoneCombo));
        RefreshProcesses();
        UpdateClipValueText(ClipSecondsSlider.Value);
    }

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e) => RefreshProcesses();
    private void RefreshMicrophones_Click(object sender, RoutedEventArgs e) => PopulateMicrophones(SelectedTag(MicrophoneCombo));
    private void PopulateMicrophones(string selectedDeviceId)
    {
        if (MicrophoneCombo is null) return;
        MicrophoneCombo.Items.Clear();
        var none = new ComboBoxItem { Content = LocalizationService.T("マイクを録音しない（デフォルト）"), Tag = string.Empty };
        MicrophoneCombo.Items.Add(none);
        ComboBoxItem? selected = string.IsNullOrWhiteSpace(selectedDeviceId) ? none : null;
        try
        {
            foreach (var device in AudioCaptureService.ListMicrophones())
            {
                var defaultSuffix = LocalizationService.CurrentLanguage switch { AppLanguage.English => "  (Windows default)", AppLanguage.Korean => "  (Windows 기본값)", AppLanguage.ChineseSimplified => "  （Windows 默认）", AppLanguage.ChineseTraditional => "  （Windows 預設）", _ => "  （Windowsの既定）" };
                var label = device.Name + (device.IsDefault ? defaultSuffix : string.Empty);
                var item = new ComboBoxItem { Content = label, Tag = device.Id, ToolTip = device.Name };
                MicrophoneCombo.Items.Add(item);
                if (string.Equals(device.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase)) selected = item;
            }
        }
        catch (Exception ex) { ValidationText.Text = "マイク一覧を取得できません: " + ex.Message; }
        MicrophoneCombo.SelectedItem = selected ?? none;
    }

    private void RefreshProcesses()
    {
        if (ProcessList is not null)
        {
            var currentBoxes = ProcessList.Children.OfType<CheckBox>().ToList();
            var selected = currentBoxes.Count == 0
                ? new HashSet<string>(_working.SelectedApplications, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(currentBoxes.Where(box => box.IsChecked == true).Select(box => box.Tag).OfType<string>(), StringComparer.OrdinalIgnoreCase);
            ProcessList.Children.Clear();
            foreach (var process in ProcessCatalog.ListAvailable(selected))
            {
                var box = new CheckBox { Content = process.DisplayName, Tag = process.ProcessName, IsChecked = selected.Contains(process.ProcessName), Margin = new Thickness(2, 6, 2, 6), ToolTip = $"PID {process.ProcessId}" };
                ProcessList.Children.Add(box);
            }
            var visibleNames = ProcessList.Children.OfType<CheckBox>().Select(box => (string)box.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in selected.Where(name => !visibleNames.Contains(name)))
                ProcessList.Children.Add(new CheckBox { Content = name + "  ·  " + LocalizationService.T("未起動"), Tag = name, IsChecked = true, Margin = new Thickness(2, 6, 2, 6) });
            if (ProcessList.Children.Count == 0) ProcessList.Children.Add(new TextBlock { Text = LocalizationService.T("選択できるアプリが見つかりません。"), Foreground = new SolidColorBrush(Color.FromRgb(153, 162, 179)), Margin = new Thickness(4) });
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "動画の保存先を選択", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(OutputDirectoryBox.Text)) dialog.InitialDirectory = OutputDirectoryBox.Text;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) OutputDirectoryBox.Text = dialog.SelectedPath;
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e) { try { Directory.CreateDirectory(OutputDirectoryBox.Text); Process.Start(new ProcessStartInfo(OutputDirectoryBox.Text) { UseShellExecute = true }); } catch (Exception ex) { ValidationText.Text = ex.Message; } }
    private void BrowseClipCacheFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = LocalizationService.T("一時データの保存先"), UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(ClipCacheDirectoryBox.Text)) dialog.InitialDirectory = ClipCacheDirectoryBox.Text;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) ClipCacheDirectoryBox.Text = dialog.SelectedPath;
    }
    private void OpenClipCacheFolder_Click(object sender, RoutedEventArgs e) { try { var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ClipCacheDirectoryBox.Text.Trim())); Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { ValidationText.Text = ex.Message; } }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var gesture = new HotkeyGesture { Key = key.ToString(), Control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), Alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt), Shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Windows = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin) };
        if (sender == CaptureHotkeyBox) _captureHotkey = gesture; else _clipHotkey = gesture; UpdateHotkeyText();
    }
    private void ResetHotkeys_Click(object sender, RoutedEventArgs e) { _captureHotkey = HotkeyGesture.CaptureDefault(); _clipHotkey = HotkeyGesture.ClipDefault(); UpdateHotkeyText(); }
    private void UpdateHotkeyText() { CaptureHotkeyBox.Text = _captureHotkey.ToString(); ClipHotkeyBox.Text = _clipHotkey.ToString(); }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ValidationText.Text = string.Empty;
            var output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(OutputDirectoryBox.Text.Trim())); Directory.CreateDirectory(output);
            var probe = Path.Combine(output, ".lumacapture-write-test"); using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            var clipCache = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ClipCacheDirectoryBox.Text.Trim()));
            if (string.Equals(Path.TrimEndingDirectorySeparator(clipCache), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(clipCache) ?? string.Empty), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("ドライブ直下は一時データの保存先に指定できません。");
            Directory.CreateDirectory(clipCache); var cacheProbe = Path.Combine(clipCache, ".lumacapture-write-test"); using (File.Create(cacheProbe, 1, FileOptions.DeleteOnClose)) { }
            if (!double.TryParse(WarningGbBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var warning) || warning < 0.5) throw new InvalidOperationException("容量通知は 0.5 GB 以上で入力してください。");
            if (!double.TryParse(AutoStopGbBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var autoStop) || autoStop < 0.25 || autoStop > warning) throw new InvalidOperationException("自動停止容量は 0.25 GB 以上、通知容量以下にしてください。");
            if (_captureHotkey.ToString() == _clipHotkey.ToString()) throw new InvalidOperationException("2つのホットキーには別の組み合わせを指定してください。");
            var appNames = ProcessList.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => x.Tag?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var audioMode = Enum.Parse<AudioCaptureMode>(SelectedTag(AudioModeCombo));
            // Empty allow-list means no application audio, not all-PC audio.
            if ((CaptureAreaCombo.SelectedItem as ComboBoxItem)?.Tag is not CaptureTarget captureTarget) throw new InvalidOperationException("録画する画面を選択してください。");
            _working.OutputDirectory = output; _working.CaptureCursor = CursorCheck.IsChecked == true; _working.ShowRecIndicator = RecIndicatorCheck.IsChecked == true; _working.AlwaysShowCapturePanel = AlwaysShowPanelCheck.IsChecked == true; _working.LaunchAtStartup = StartupCheck.IsChecked == true; _working.LowSpaceWarningGb = warning; _working.AutoStopGb = autoStop;
            _working.CaptureArea = captureTarget.Area; _working.CaptureDisplayDeviceName = captureTarget.DeviceName; _working.CaptureApplicationProcessName = captureTarget.ProcessName; _working.Resolution = Enum.Parse<VideoResolution>(SelectedTag(ResolutionCombo)); _working.Quality = Enum.Parse<VideoQuality>(SelectedTag(QualityCombo)); _working.AudioCaptureMode = audioMode; _working.AudioOutputMode = Enum.Parse<AudioOutputMode>(SelectedTag(AudioOutputCombo)); _working.MicrophoneDeviceId = SelectedTag(MicrophoneCombo); _working.SelectedApplications = appNames; _working.ClipBufferEnabled = ClipEnabledCheck.IsChecked == true; _working.ClipDisplayDeviceName = SelectedTag(ClipDisplayCombo); _working.ClipCacheDirectory = clipCache; _working.ConfirmClipOnExit = ConfirmClipOnExitCheck.IsChecked == true; _working.ClipSeconds = SelectedClipDuration(ClipSecondsSlider.Value); _working.Language = Enum.Parse<AppLanguage>(SelectedTag(LanguageCombo)); _working.CaptureHotkey = _captureHotkey.Clone(); _working.ClipHotkey = _clipHotkey.Clone(); _working.Normalize();
            ResultSettings = _working.Clone(); DialogResult = true;
        }
        catch (Exception ex) { ValidationText.Text = ex.Message; }
    }
    internal void SaveForTest() => Save_Click(this, new RoutedEventArgs());
    internal void RefreshForTest() => RefreshProcesses();
    private void OpenCaptura_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com/mathewsachin/captura") { UseShellExecute = true });
    private void Cancel_Click(object sender, RoutedEventArgs e) { LocalizationService.SetLanguage(_originalLanguage); DialogResult = false; Close(); }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

    private sealed record CaptureTarget(CaptureArea Area, string DeviceName, string ProcessName);
}
