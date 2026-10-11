using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public static class ApplicationSelectionPolicy
{
    public static bool ClearIfUnavailable(AppSettings settings)
    {
        if (settings.CaptureArea != CaptureArea.SpecificApplication || ProcessCatalog.HasApplicationWindow(settings.CaptureApplicationProcessName)) return false;
        settings.CaptureArea = CaptureArea.PrimaryDisplay;
        settings.CaptureApplicationProcessName = string.Empty;
        settings.CaptureDisplayDeviceName = string.Empty;
        return true;
    }
}
