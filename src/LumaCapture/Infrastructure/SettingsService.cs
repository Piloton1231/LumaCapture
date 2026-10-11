using System.Text.Json;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string SettingsDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaCapture");
    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings { Language = LocalizationService.DetectSystemLanguage() };
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(AppSettings.CaptureDisplayDeviceName), out _) && settings.CaptureArea == CaptureArea.AllDisplays)
                settings.CaptureArea = CaptureArea.PrimaryDisplay;
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings { Language = LocalizationService.DetectSystemLanguage() };
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(SettingsDirectory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }
}
