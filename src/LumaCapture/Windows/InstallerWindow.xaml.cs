using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using LumaCapture.Models;
using LumaCapture.Infrastructure;

namespace LumaCapture.Windows;

public enum InstallerMode { Install, UninstallConfirm, FinishUninstall }

public partial class InstallerWindow : Window
{
    private readonly InstallerMode _mode; private readonly string? _target; private readonly InstallerService _service = new(); private bool _completed; private bool _busy;
    public InstallerWindow(InstallerMode mode, string? target = null)
    {
        InitializeComponent(); LocalizationService.Apply(this); _mode = mode; _target = target;
        LanguageCombo.SelectedIndex = (int)LocalizationService.CurrentLanguage;
        if (mode == InstallerMode.UninstallConfirm) { WindowTitle.Text = L("LumaCapture アンインストール"); HeadingText.Text = L("LumaCapture を削除しますか？"); DescriptionText.Text = L("アプリ本体を削除します。保存済みの動画と個人設定は残ります。"); LogoGlyph.Text = "−"; OptionsPanel.Visibility = Visibility.Collapsed; PrimaryButton.Content = L("削除"); PrimaryButton.Style = (Style)FindResource("DangerButton"); }
        else if (mode == InstallerMode.FinishUninstall) { WindowTitle.Text = L("LumaCapture アンインストール"); HeadingText.Text = L("LumaCapture を削除しています"); DescriptionText.Text = L("しばらくお待ちください。"); LogoGlyph.Text = "−"; OptionsPanel.Visibility = Visibility.Collapsed; SecondaryButton.Visibility = Visibility.Collapsed; PrimaryButton.Visibility = Visibility.Collapsed; CloseButton.IsEnabled = false; ProgressTrack.Visibility = Visibility.Visible; Loaded += async (_, _) => await FinishUninstallAsync(); }
    }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem { Tag: string value } && Enum.TryParse<AppLanguage>(value, out var language))
        {
            LocalizationService.SetLanguage(language);
            LocalizationService.Apply(this);
            if (_completed && _mode == InstallerMode.Install)
            {
                var service = new SettingsService(); var settings = service.Load(); settings.Language = language; service.Save(settings);
                HeadingText.Text = L("インストールが完了しました");
                DescriptionText.Text = L("LumaCapture はシステムトレイに常駐します。");
                PrimaryButton.Content = L("起動");
            }
        }
    }

    internal void SelectLanguageForTest(AppLanguage language) => LanguageCombo.SelectedIndex = (int)language;
    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_completed) { if (_mode == InstallerMode.Install) _service.StartInstalledApp(); Close(); return; }
        if (_mode == InstallerMode.Install) await InstallAsync(); else if (_mode == InstallerMode.UninstallConfirm) BeginUninstall();
    }
    private async Task InstallAsync()
    {
        SetBusy(true, L("インストールを準備しています…"));
        try { await _service.InstallAsync(DesktopCheck.IsChecked == true, StartupCheck.IsChecked == true, new Progress<double>(SetProgress)); var settingsService = new SettingsService(); var settings = settingsService.Load(); settings.Language = LocalizationService.CurrentLanguage; settings.LaunchAtStartup = StartupCheck.IsChecked == true; settingsService.Save(settings); _completed = true; HeadingText.Text = L("インストールが完了しました"); DescriptionText.Text = L("LumaCapture はシステムトレイに常駐します。"); StatusText.Text = _service.InstallDirectory; PrimaryButton.Content = L("起動"); PrimaryButton.Visibility = Visibility.Visible; SecondaryButton.Visibility = Visibility.Collapsed; CloseButton.IsEnabled = true; LanguageCombo.IsEnabled = true; _busy = false; }
        catch (Exception ex) { HeadingText.Text = L("インストールできませんでした"); SetBusy(false, ex.Message); PrimaryButton.Content = L("再試行"); }
    }
    private void BeginUninstall()
    {
        try { var temp = _service.CreateTemporaryUninstaller(); var info = new ProcessStartInfo(temp) { UseShellExecute = false }; info.ArgumentList.Add("--finish-uninstall"); info.ArgumentList.Add(_service.InstallDirectory); Process.Start(info); Application.Current.Shutdown(); } catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private async Task FinishUninstallAsync()
    {
        try { await _service.FinishUninstallAsync(_target ?? _service.InstallDirectory, new Progress<double>(SetProgress)); _completed = true; HeadingText.Text = L("アンインストールが完了しました"); DescriptionText.Text = L("保存済みの動画と個人設定は残っています。"); StatusText.Text = L("ご利用ありがとうございました。"); PrimaryButton.Content = L("閉じる"); PrimaryButton.Visibility = Visibility.Visible; PrimaryButton.IsEnabled = true; CloseButton.IsEnabled = true; }
        catch (Exception ex) { HeadingText.Text = L("削除を完了できませんでした"); StatusText.Text = ex.Message; PrimaryButton.Content = L("閉じる"); PrimaryButton.Visibility = Visibility.Visible; PrimaryButton.IsEnabled = true; CloseButton.IsEnabled = true; _completed = true; }
    }
    private void SetBusy(bool busy, string status) { _busy = busy; LanguageCombo.IsEnabled = !busy; OptionsPanel.IsEnabled = !busy; SecondaryButton.IsEnabled = !busy; PrimaryButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible; CloseButton.IsEnabled = !busy; ProgressTrack.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; StatusText.Text = status; }
    private void SetProgress(double value) { ProgressFill.Width = Math.Max(0, 526 * Math.Clamp(value, 0, 1)); StatusText.Text = $"{Math.Round(value * 100)}%"; }
    private void Close_Click(object sender, RoutedEventArgs e) { if (!_busy) Close(); }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private static string L(string text) => LocalizationService.T(text);
}
