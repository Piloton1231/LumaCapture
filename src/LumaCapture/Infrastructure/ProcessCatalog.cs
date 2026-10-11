using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public static class ProcessCatalog
{
    public static IReadOnlyList<CaptureProcessInfo> ListAvailable(IEnumerable<string>? keepNames = null)
    {
        var keep = new HashSet<string>(keepNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var result = new List<CaptureProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.HasExited) continue;
                    var name = process.ProcessName;
                    var title = process.MainWindowTitle;
                    var handle = process.MainWindowHandle;
                    var isKept = keep.Contains(name);
                    if (handle != IntPtr.Zero && IsIconic(handle)) continue;
                    if (!isKept && !IsSelectableWindow(handle)) continue;
                    if (string.IsNullOrWhiteSpace(title) && !isKept) continue;
                    var display = string.IsNullOrWhiteSpace(title) ? name : $"{name}  —  {title}";
                    result.Add(new CaptureProcessInfo(process.Id, name, display));
                }
                catch { }
            }
        }
        return result.OrderBy(x => x.ProcessName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.DisplayName).ToList();
    }

    public static IReadOnlyList<CaptureProcessInfo> ResolveSelected(IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return ListAvailable(wanted)
            .Where(x => wanted.Contains(x.ProcessName))
            .GroupBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(p => p.DisplayName.Length).First())
            .ToList();
    }

    public static string SafeLabel(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "audio" : safe;
    }

    internal static bool IsSelectableWindow(IntPtr handle) => handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle);

    public static bool HasApplicationWindow(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle is { } handle && handle != IntPtr.Zero && IsWindow(handle)) return true;
                }
                catch { }
            }
        }
        return false;
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
}
