using System.Windows;
using LumaCapture.Infrastructure;

namespace LumaCapture.Windows;

public enum ClipExitChoice { Cancel, Discard, Save }

public partial class ClipExitPromptWindow : Window
{
    public ClipExitChoice Choice { get; private set; } = ClipExitChoice.Cancel;

    public ClipExitPromptWindow(int seconds)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        if (FindName("ClipLengthText") is System.Windows.Controls.TextBlock length) length.Text = LocalizationService.Seconds(seconds);
        SourceInitialized += (_, _) => WindowProtection.ExcludeFromCapture(this);
    }

    private void Save_Click(object sender, RoutedEventArgs e) { Choice = ClipExitChoice.Save; DialogResult = true; }
    private void Discard_Click(object sender, RoutedEventArgs e) { Choice = ClipExitChoice.Discard; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Choice = ClipExitChoice.Cancel; DialogResult = false; }
}
