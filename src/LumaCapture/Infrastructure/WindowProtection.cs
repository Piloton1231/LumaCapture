using System.Windows;
using System.Windows.Interop;

namespace LumaCapture.Infrastructure;

public static class WindowProtection
{
    public static void ExcludeFromCapture(Window window)
    {
        if (Environment.GetEnvironmentVariable("LUMACAPTURE_UI_REVIEW") == "1") return;
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);
        }
        catch { }
    }
}
