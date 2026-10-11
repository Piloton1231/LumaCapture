using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LumaCapture.Infrastructure;

namespace LumaCapture.Windows;

public sealed class CaptureTargetOverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolwindow = 0x80;
    private const int WsExNoactivate = 0x08000000;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    public CaptureTargetOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 60, 88)),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(5),
            Background = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible = false
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
            SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style | WsExTransparent | WsExToolwindow | WsExNoactivate));
            WindowProtection.ExcludeFromCapture(this);
        };
    }

    public void ShowBounds(Rectangle bounds)
    {
        if (!IsVisible) Show();
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, HwndTopmost, bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), SwpNoactivate | SwpShowwindow);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
