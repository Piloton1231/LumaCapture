namespace LumaCapture.Models;

public enum VideoQuality { High, Standard, Compact }
public enum VideoResolution { Original, P2160, P1440, P1080, P720 }
public enum CaptureArea { AllDisplays, PrimaryDisplay, SpecificDisplay, SpecificApplication }
public enum AudioCaptureMode { SystemAudio, SelectedApplications }
public enum AudioOutputMode { EmbedInVideo, SplitMixed, SplitPerApplication }
public enum AppLanguage { Japanese, English, Korean, ChineseSimplified, ChineseTraditional }

public sealed class HotkeyGesture
{
    public string Key { get; set; } = "C";
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Windows { get; set; }

    public static HotkeyGesture CaptureDefault() => new() { Key = "C", Windows = true, Shift = true };
    public static HotkeyGesture ClipDefault() => new() { Key = "C", Alt = true };

    public HotkeyGesture Clone() => new() { Key = Key, Control = Control, Alt = Alt, Shift = Shift, Windows = Windows };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Windows) parts.Add("Windows");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

public sealed class AppSettings
{
    public static string DefaultClipCacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaCapture", "clip-buffer");
    public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "LumaCapture");
    public VideoQuality Quality { get; set; } = VideoQuality.Standard;
    public VideoResolution Resolution { get; set; } = VideoResolution.P1080;
    public CaptureArea CaptureArea { get; set; } = CaptureArea.PrimaryDisplay;
    public string CaptureDisplayDeviceName { get; set; } = string.Empty;
    public string CaptureApplicationProcessName { get; set; } = string.Empty;
    public bool CaptureCursor { get; set; } = true;
    public bool ShowRecIndicator { get; set; } = true;
    public bool AlwaysShowCapturePanel { get; set; }
    public double CapturePanelLeft { get; set; } = -1;
    public double CapturePanelTop { get; set; } = -1;
    public bool LaunchAtStartup { get; set; } = true;
    public AudioCaptureMode AudioCaptureMode { get; set; } = AudioCaptureMode.SystemAudio;
    public AudioOutputMode AudioOutputMode { get; set; } = AudioOutputMode.EmbedInVideo;
    public string MicrophoneDeviceId { get; set; } = string.Empty;
    public List<string> SelectedApplications { get; set; } = new();
    public bool ClipBufferEnabled { get; set; } = true;
    public int ClipSeconds { get; set; } = 60;
    public string ClipDisplayDeviceName { get; set; } = string.Empty;
    public string ClipCacheDirectory { get; set; } = DefaultClipCacheDirectory;
    public bool ConfirmClipOnExit { get; set; } = true;
    public AppLanguage Language { get; set; } = AppLanguage.Japanese;
    public double LowSpaceWarningGb { get; set; } = 5;
    public double AutoStopGb { get; set; } = 2;
    public HotkeyGesture CaptureHotkey { get; set; } = HotkeyGesture.CaptureDefault();
    public HotkeyGesture ClipHotkey { get; set; } = HotkeyGesture.ClipDefault();

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "LumaCapture");
        ClipSeconds = Math.Clamp(ClipSeconds, 30, 600);
        LowSpaceWarningGb = Math.Clamp(LowSpaceWarningGb, 0.5, 1000);
        AutoStopGb = Math.Clamp(AutoStopGb, 0.25, LowSpaceWarningGb);
        CaptureHotkey ??= HotkeyGesture.CaptureDefault();
        ClipHotkey ??= HotkeyGesture.ClipDefault();
        SelectedApplications ??= new List<string>();
        CaptureDisplayDeviceName ??= string.Empty;
        CaptureApplicationProcessName ??= string.Empty;
        MicrophoneDeviceId ??= string.Empty;
        ClipDisplayDeviceName ??= string.Empty;
        if (string.IsNullOrWhiteSpace(ClipCacheDirectory)) ClipCacheDirectory = DefaultClipCacheDirectory;
        if (CaptureArea == CaptureArea.SpecificDisplay && string.IsNullOrWhiteSpace(CaptureDisplayDeviceName))
            CaptureArea = CaptureArea.PrimaryDisplay;
        if (CaptureArea == CaptureArea.SpecificApplication && string.IsNullOrWhiteSpace(CaptureApplicationProcessName))
            CaptureArea = CaptureArea.PrimaryDisplay;
    }

    public AppSettings Clone() => new()
    {
        OutputDirectory = OutputDirectory,
        Quality = Quality,
        Resolution = Resolution,
        CaptureArea = CaptureArea,
        CaptureDisplayDeviceName = CaptureDisplayDeviceName,
        CaptureApplicationProcessName = CaptureApplicationProcessName,
        CaptureCursor = CaptureCursor,
        ShowRecIndicator = ShowRecIndicator,
        AlwaysShowCapturePanel = AlwaysShowCapturePanel,
        CapturePanelLeft = CapturePanelLeft,
        CapturePanelTop = CapturePanelTop,
        LaunchAtStartup = LaunchAtStartup,
        AudioCaptureMode = AudioCaptureMode,
        AudioOutputMode = AudioOutputMode,
        MicrophoneDeviceId = MicrophoneDeviceId,
        SelectedApplications = SelectedApplications.ToList(),
        ClipBufferEnabled = ClipBufferEnabled,
        ClipSeconds = ClipSeconds,
        ClipDisplayDeviceName = ClipDisplayDeviceName,
        ClipCacheDirectory = ClipCacheDirectory,
        ConfirmClipOnExit = ConfirmClipOnExit,
        Language = Language,
        LowSpaceWarningGb = LowSpaceWarningGb,
        AutoStopGb = AutoStopGb,
        CaptureHotkey = CaptureHotkey.Clone(),
        ClipHotkey = ClipHotkey.Clone()
    };
}

public sealed record AudioTrackFile(string Label, string Path, bool IsMicrophone = false, int Channels = 2);
public sealed record CaptureProcessInfo(int ProcessId, string ProcessName, string DisplayName);
public sealed record HotkeyRegistration(bool CaptureRegistered, bool ClipRegistered);
