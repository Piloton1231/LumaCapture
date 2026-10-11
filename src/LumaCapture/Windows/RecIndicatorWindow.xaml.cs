using System.Windows;
using LumaCapture.Infrastructure;

namespace LumaCapture.Windows;

public partial class RecIndicatorWindow : Window
{
    public RecIndicatorWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowProtection.ExcludeFromCapture(this);
    }
    public void ShowTopLeft()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Left = screen.Left + 18; Top = screen.Top + 18; Show();
    }
    public void Update(TimeSpan elapsed) => TimeText.Text = elapsed.ToString(@"mm\:ss");
}
