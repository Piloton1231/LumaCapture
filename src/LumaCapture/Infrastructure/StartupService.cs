using Microsoft.Win32;

namespace LumaCapture.Infrastructure;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Apply(bool enabled, string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("LumaCapture", $"\"{executable}\"");
        else key.DeleteValue("LumaCapture", false);
    }
}
