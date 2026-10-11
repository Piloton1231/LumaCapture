using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed record ResolvedCaptureTarget(System.Windows.Forms.Screen Screen, int ScreenIndex, Rectangle Bounds, Rectangle? CropBounds, string Label);
public sealed record ResolvedApplicationWindow(IntPtr Handle, Rectangle Bounds, int ProcessId);

public static class CaptureTargetResolver
{
    private const int DwmwaExtendedFrameBounds = 9;

    public static IReadOnlyList<ResolvedCaptureTarget> Resolve(AppSettings settings)
    {
        var screens = System.Windows.Forms.Screen.AllScreens.Select((screen, index) => (screen, index)).ToArray();
        if (screens.Length == 0) return Array.Empty<ResolvedCaptureTarget>();
        if (settings.CaptureArea == CaptureArea.AllDisplays)
            return screens.Select(item => new ResolvedCaptureTarget(item.screen, item.index, item.screen.Bounds, null, item.screen.DeviceName)).ToList();
        if (settings.CaptureArea == CaptureArea.SpecificApplication)
        {
            var appBounds = ResolveApplicationBounds(settings.CaptureApplicationProcessName);
            var screen = System.Windows.Forms.Screen.FromRectangle(appBounds);
            var indexed = screens.FirstOrDefault(item => string.Equals(item.screen.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
            var intersection = Rectangle.Intersect(appBounds, indexed.screen.Bounds);
            if (intersection.Width < 16 || intersection.Height < 16)
                throw new InvalidOperationException(LocalizationService.T("録画対象のアプリが画面上に見つかりません。"));
            return new[] { new ResolvedCaptureTarget(indexed.screen, indexed.index, intersection, intersection, settings.CaptureApplicationProcessName) };
        }
        if (settings.CaptureArea == CaptureArea.SpecificDisplay)
        {
            var specific = screens.FirstOrDefault(item => string.Equals(item.screen.DeviceName, settings.CaptureDisplayDeviceName, StringComparison.OrdinalIgnoreCase));
            if (specific.screen is not null) return new[] { new ResolvedCaptureTarget(specific.screen, specific.index, specific.screen.Bounds, null, specific.screen.DeviceName) };
        }
        var primary = screens.FirstOrDefault(item => item.screen.Primary);
        if (primary.screen is null) primary = screens[0];
        return new[] { new ResolvedCaptureTarget(primary.screen, primary.index, primary.screen.Bounds, null, primary.screen.DeviceName) };
    }

    public static bool TryResolveApplicationBounds(string processName, out Rectangle bounds)
    {
        try { bounds = ResolveApplicationBounds(processName); return true; }
        catch { bounds = Rectangle.Empty; return false; }
    }

    public static bool TryResolveApplicationWindow(string processName, out ResolvedApplicationWindow? window)
    {
        try { window = ResolveApplicationWindow(processName); return true; }
        catch { window = null; return false; }
    }

    public static bool TryResolveApplicationWindow(string processName, int processId, out ResolvedApplicationWindow? window)
    {
        try { window = ResolveApplicationWindow(processName, processId); return true; }
        catch { window = null; return false; }
    }

    public static bool TryResolveForegroundApplicationWindow(string processName, int processId, out ResolvedApplicationWindow? window)
    {
        window = null;
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle)) return false;
            GetWindowThreadProcessId(handle, out var foregroundProcessId);
            if (foregroundProcessId != processId) return false;
            using var process = Process.GetProcessById((int)foregroundProcessId);
            if (!string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!GetWindowRect(handle, out var rect)) return false;
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < 16 || bounds.Height < 16) return false;
            window = new ResolvedApplicationWindow(handle, bounds, (int)foregroundProcessId);
            return true;
        }
        catch { return false; }
    }

    public static bool IsProcessRunning(int processId)
    {
        try { using var process = Process.GetProcessById(processId); return !process.HasExited; }
        catch { return false; }
    }

    public static Rectangle ResolveApplicationBounds(string processName) => ResolveApplicationWindow(processName).Bounds;

    public static ResolvedApplicationWindow ResolveApplicationWindow(string processName) => ResolveApplicationWindow(processName, null);

    private static ResolvedApplicationWindow ResolveApplicationWindow(string processName, int? requiredProcessId)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new InvalidOperationException(LocalizationService.T("録画するアプリを選択してください。"));
        var candidates = new List<ResolvedApplicationWindow>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (requiredProcessId.HasValue && process.Id != requiredProcessId.Value) continue;
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle)) continue;
                    if (DwmGetWindowAttribute(handle, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<NativeRect>()) != 0 && !GetWindowRect(handle, out rect)) continue;
                    var candidate = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    if (candidate.Width >= 16 && candidate.Height >= 16) candidates.Add(new ResolvedApplicationWindow(handle, candidate, process.Id));
                }
                catch { }
            }
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException(LocalizationService.T("録画対象のアプリが起動していないか、最小化されています。"));
        return candidates.OrderByDescending(value => (long)value.Bounds.Width * value.Bounds.Height).First();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
}
