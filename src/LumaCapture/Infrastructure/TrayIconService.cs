using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumaCapture.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private Icon? _ownedIcon;
    public event EventHandler? LeftClicked;
    public event EventHandler? RightClicked;

    public TrayIconService()
    {
        _ownedIcon = CreateIcon(false);
        _icon = new NotifyIcon { Icon = _ownedIcon, Text = Tooltip(false), Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClicked?.Invoke(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Right) RightClicked?.Invoke(this, EventArgs.Empty);
        };
    }

    public void SetRecording(bool recording)
    {
        var next = CreateIcon(recording);
        _icon.Icon = next;
        _icon.Text = Tooltip(recording);
        _ownedIcon?.Dispose();
        _ownedIcon = next;
    }

    public void RefreshText(bool recording) => _icon.Text = Tooltip(recording);

    private static string Tooltip(bool recording) => LocalizationService.CurrentLanguage switch
    {
        LumaCapture.Models.AppLanguage.English => recording ? "LumaCapture — Recording" : "LumaCapture — Ready",
        LumaCapture.Models.AppLanguage.Korean => recording ? "LumaCapture — 녹화 중" : "LumaCapture — 대기 중",
        LumaCapture.Models.AppLanguage.ChineseSimplified => recording ? "LumaCapture — 正在录制" : "LumaCapture — 就绪",
        LumaCapture.Models.AppLanguage.ChineseTraditional => recording ? "LumaCapture — 正在錄製" : "LumaCapture — 就緒",
        _ => recording ? "LumaCapture — 録画中" : "LumaCapture — 待機中"
    };

    internal static Bitmap RenderBitmap(bool recording)
    {
        var bitmap = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        using (var gradient = new LinearGradientBrush(
                   new Rectangle(4, 4, 56, 56),
                   Color.FromArgb(139, 108, 255),
                   Color.FromArgb(64, 206, 255),
                   45f))
        {
            graphics.FillEllipse(gradient, 4, 4, 56, 56);
        }

        using (var center = new SolidBrush(Color.FromArgb(20, 22, 30)))
            graphics.FillEllipse(center, 13, 13, 38, 38);

        using (var logo = new Pen(Color.White, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLine(logo, 23, 20, 23, 42);
            graphics.DrawLine(logo, 23, 42, 42, 42);
        }

        if (recording)
        {
            using var recordDot = new SolidBrush(Color.FromArgb(255, 82, 109));
            using var recordOutline = new Pen(Color.White, 2f);
            graphics.FillEllipse(recordDot, 35, 16, 14, 14);
            graphics.DrawEllipse(recordOutline, 35, 16, 14, 14);
        }
        else
        {
            using var recordRing = new Pen(Color.White, 3.5f);
            graphics.DrawEllipse(recordRing, 37, 18, 10, 10);
        }

        return bitmap;
    }

    internal static bool ValidateRenderedIcons()
    {
        using var idle = RenderBitmap(false);
        using var recording = RenderBitmap(true);
        var transparentCorner = idle.GetPixel(0, 0).A == 0;
        var darkCenter = idle.GetPixel(32, 32);
        var idleBadge = idle.GetPixel(42, 18);
        var recordingBadge = recording.GetPixel(42, 23);
        return transparentCorner
            && darkCenter.R < 50 && darkCenter.G < 50 && darkCenter.B < 50
            && idleBadge.R > 200 && idleBadge.G > 200 && idleBadge.B > 200
            && recordingBadge.R > 200 && recordingBadge.G < 130 && recordingBadge.B < 160;
    }

    private static Icon CreateIcon(bool recording)
    {
        using var bitmap = RenderBitmap(recording);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon?.Dispose();
    }
}
