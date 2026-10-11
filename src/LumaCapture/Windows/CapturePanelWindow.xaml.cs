using System.Windows;
using System.Windows.Controls;
using LumaCapture.Infrastructure;
using LumaCapture.Models;

namespace LumaCapture.Windows;

public partial class CapturePanelWindow : Window
{
    private bool _recording;
    private bool _openingSettings;
    private bool _openingPicker;
    private bool _refreshing;
    private bool _busy;
    public event EventHandler<VideoResolution>? ResolutionChanged;
    public event EventHandler<(CaptureArea Area, string Device)>? TargetChanged;
    public event EventHandler? ClipRequested;
    public event EventHandler? FolderRequested;
    public event EventHandler? AudioSettingsRequested;
    public bool AutoHide { get; set; } = true;
    public event EventHandler? RecordToggleRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AppSelectionRequested;
    public event EventHandler? PositionChangedByUser;
    public event EventHandler<VideoQuality>? QualityChanged;

    public CapturePanelWindow(VideoQuality quality)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        SetQuality(quality);
        if (!Environment.GetCommandLineArgs().Any(argument => argument.StartsWith("--preview", StringComparison.OrdinalIgnoreCase)))
            SourceInitialized += (_, _) => WindowProtection.ExcludeFromCapture(this);
    }

    public void SetQuality(VideoQuality quality)
    {
        foreach (ComboBoxItem item in QualityCombo.Items)
            if (string.Equals(item.Tag?.ToString(), quality.ToString(), StringComparison.Ordinal)) { QualityCombo.SelectedItem = item; break; }
    }

    public void UpdateRecording(bool recording, TimeSpan elapsed)
    {
        _recording = recording;
        RecordDot.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        StopSquare.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = LocalizationService.T(recording ? "録画中" : "録画を開始");
        HintText.Text = recording ? elapsed.ToString(@"hh\:mm\:ss") + "  •  " + LocalizationService.T("クリックで停止") : LocalizationService.T("クリックで開始");
        QualityCombo.IsEnabled = !recording;
        AppPickerButton.IsEnabled = !recording;
        ClipButton.IsEnabled = ClipButton.IsEnabled && !recording && !_busy;
        TargetCombo.IsEnabled = ResolutionCombo.IsEnabled = !recording && !_busy;
        if (_busy) { StatusText.Text = LocalizationService.T("処理中"); RecordButton.IsEnabled = false; QualityCombo.IsEnabled = AppPickerButton.IsEnabled = false; }
    }

    public void SetBusy(bool busy)
    {
        _busy = busy; RecordButton.IsEnabled = !busy;
        if (busy)
        {
            StatusText.Text = LocalizationService.T("処理中");
            TargetCombo.IsEnabled = ResolutionCombo.IsEnabled = QualityCombo.IsEnabled = AppPickerButton.IsEnabled = ClipButton.IsEnabled = false;
        }
    }
    public void UpdateConfiguration(AppSettings settings, bool clipsRunning)
    {
        _refreshing = true;
        try
        {
            TargetCombo.Items.Clear();
            void Add(string text, CaptureArea area, string device = "")
            {
                var item = new ComboBoxItem { Content = text, Tag = (area, device) };
                TargetCombo.Items.Add(item);
                if (settings.CaptureArea == area && (area != CaptureArea.SpecificDisplay || settings.CaptureDisplayDeviceName == device)) TargetCombo.SelectedItem = item;
            }
            Add(LocalizationService.T("メイン画面"), CaptureArea.PrimaryDisplay);
            Add(LocalizationService.T("すべての画面"), CaptureArea.AllDisplays);
            foreach (var screen in System.Windows.Forms.Screen.AllScreens) Add(screen.DeviceName, CaptureArea.SpecificDisplay, screen.DeviceName);
            if (settings.CaptureArea == CaptureArea.SpecificApplication) Add(settings.CaptureApplicationProcessName, CaptureArea.SpecificApplication);
            foreach (ComboBoxItem item in ResolutionCombo.Items) if (item.Tag?.ToString() == settings.Resolution.ToString()) ResolutionCombo.SelectedItem = item;
            AudioSummary.Text = LocalizationService.T(settings.AudioCaptureMode == AudioCaptureMode.SystemAudio ? "PC内の音をすべて録音" : "選択したアプリだけ録音");
            AudioSummary.ToolTip = settings.AudioCaptureMode == AudioCaptureMode.SystemAudio ? AudioSummary.Text : string.Join(", ", settings.SelectedApplications);
            MicSummary.Text = LocalizationService.T("マイク") + ": " + (string.IsNullOrEmpty(settings.MicrophoneDeviceId) ? "OFF" : "ON");
            ClipSummary.Text = LocalizationService.Seconds(settings.ClipSeconds) + " · " + settings.ClipHotkey + " · " + (clipsRunning ? "ON" : "OFF");
            ClipButton.IsEnabled = clipsRunning && !_recording && !_busy;
            FolderText.Text = LocalizationService.T("動画の保存先") + "  ›  " + settings.OutputDirectory;
            FolderText.ToolTip = settings.OutputDirectory;
        }
        finally { _refreshing = false; }
    }
    private void ResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_refreshing && ResolutionCombo.SelectedItem is ComboBoxItem item && Enum.TryParse<VideoResolution>(item.Tag?.ToString(), out var value)) ResolutionChanged?.Invoke(this, value); }
    private void TargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_refreshing && TargetCombo.SelectedItem is ComboBoxItem { Tag: ValueTuple<CaptureArea, string> target }) TargetChanged?.Invoke(this, target); }
    private void Clip_Click(object sender, RoutedEventArgs e) => ClipRequested?.Invoke(this, EventArgs.Empty);
    private void Folder_Click(object sender, RoutedEventArgs e) => FolderRequested?.Invoke(this, EventArgs.Empty);
    private void Audio_Click(object sender, RoutedEventArgs e) { _openingSettings = true; AudioSettingsRequested?.Invoke(this, EventArgs.Empty); }
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    public void UpdateTarget(string target)
    {
        TargetText.Text = target;
        TargetText.ToolTip = target;
    }

    public void ShowAtSavedPosition(double left, double top)
    {
        if (!IsVisible)
        {
            var valid = !(Math.Abs(left + 1) < 0.01 && Math.Abs(top + 1) < 0.01)
                && left >= System.Windows.Forms.SystemInformation.VirtualScreen.Left - Width + 40
                && left <= System.Windows.Forms.SystemInformation.VirtualScreen.Right - 40
                && top >= System.Windows.Forms.SystemInformation.VirtualScreen.Top
                && top <= System.Windows.Forms.SystemInformation.VirtualScreen.Bottom - 40;
            if (valid) { Left = left; Top = top; }
            else
            {
                var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
                Left = screen.Left + (screen.Width - Width) / 2;
                Top = screen.Top + Math.Max(22, screen.Height * 0.12);
            }
        }
        Show(); Activate();
    }

    public void ShowCentered() => ShowAtSavedPosition(-1, -1);

    private void Record_Click(object sender, RoutedEventArgs e) => RecordToggleRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) { _openingSettings = true; SettingsRequested?.Invoke(this, EventArgs.Empty); }
    private void AppPicker_Click(object sender, RoutedEventArgs e) { _openingPicker = true; AppSelectionRequested?.Invoke(this, EventArgs.Empty); }
    private void DragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        try { DragMove(); PositionChangedByUser?.Invoke(this, EventArgs.Empty); } catch { }
    }
    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QualityCombo.SelectedItem is ComboBoxItem item && Enum.TryParse<VideoQuality>(item.Tag?.ToString(), out var quality)) QualityChanged?.Invoke(this, quality);
    }
    private void Window_Deactivated(object? sender, EventArgs e) { if (AutoHide && !_recording && !_openingSettings && !_openingPicker) Hide(); _openingSettings = false; _openingPicker = false; }
}
