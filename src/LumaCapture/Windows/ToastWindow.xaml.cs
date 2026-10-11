using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LumaCapture.Infrastructure;

namespace LumaCapture.Windows;

public partial class ToastWindow : Window
{
    public ToastWindow(string title, string message, bool success, int duration = 3200)
    {
        InitializeComponent(); TitleText.Text = title; MessageText.Text = message;
        if (!success) { StatusCircle.Background = new SolidColorBrush(Color.FromRgb(255, 82, 109)); StatusGlyph.Text = "!"; }
        SourceInitialized += (_, _) => WindowProtection.ExcludeFromCapture(this);
        Loaded += (_, _) => { var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(duration) }; timer.Tick += (_, _) => { timer.Stop(); Close(); }; timer.Start(); };
    }
    public void ShowNearTaskbar()
    {
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Left = area.Right - Width - 18; Top = area.Bottom - ActualHeight - 18; Show();
        Dispatcher.BeginInvoke(() => Top = area.Bottom - ActualHeight - 18);
    }
}
