using System.Windows;
using LumaCapture.Infrastructure;

namespace LumaCapture.Windows;

public partial class TrayMenuWindow : Window
{
    private bool _closing;

    public event EventHandler? ShowPanelRequested; public event EventHandler? RecordRequested; public event EventHandler? ClipRequested; public event EventHandler? OpenFolderRequested; public event EventHandler? SettingsRequested; public event EventHandler? ExitRequested;
    public TrayMenuWindow(bool recording, int clipSeconds, bool clipEnabled)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        RecordButton.Content = LocalizationService.T(recording ? "録画を停止" : "録画を開始");
        StatusText.Text = recording ? "● " + LocalizationService.T("録画中") : LocalizationService.T("録画とクリップをすぐに。");
        ClipText.Text = clipEnabled ? RecentClipText(clipSeconds) : LocalizationService.T("クリップは無効");
        SourceInitialized += (_, _) => WindowProtection.ExcludeFromCapture(this);
    }
    private static string RecentClipText(int seconds) => LocalizationService.CurrentLanguage switch { LumaCapture.Models.AppLanguage.English => $"Save last {seconds} sec", LumaCapture.Models.AppLanguage.Korean => $"최근 {seconds}초 저장", LumaCapture.Models.AppLanguage.ChineseSimplified => $"保存最近 {seconds} 秒", LumaCapture.Models.AppLanguage.ChineseTraditional => $"儲存最近 {seconds} 秒", _ => $"直前{seconds}秒を保存" };
    public void ShowNearCursor() { var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea; Left = Math.Clamp(System.Windows.Forms.Cursor.Position.X - Width + 18, area.Left + 8, area.Right - Width - 8); Top = area.Bottom - ActualHeight - 10; Show(); Dispatcher.BeginInvoke(() => Top = area.Bottom - ActualHeight - 10); Activate(); }
    public void RequestClose()
    {
        if (_closing) return;
        _closing = true;
        Deactivated -= Window_Deactivated;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        base.OnClosed(e);
    }

    private void ShowPanel_Click(object sender, RoutedEventArgs e) => ShowPanelRequested?.Invoke(this, EventArgs.Empty); private void Record_Click(object sender, RoutedEventArgs e) => RecordRequested?.Invoke(this, EventArgs.Empty); private void Clip_Click(object sender, RoutedEventArgs e) => ClipRequested?.Invoke(this, EventArgs.Empty); private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(this, EventArgs.Empty); private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty); private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty); private void Window_Deactivated(object? sender, EventArgs e) => RequestClose();
}
