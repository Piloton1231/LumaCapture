using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed class StorageMonitorService : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool> _isRecording;
    private readonly System.Threading.Timer _timer;
    private bool _warningRaised;
    public event EventHandler<double>? LowSpace;
    public event EventHandler<double>? AutoStopRequired;

    public StorageMonitorService(Func<AppSettings> settings, Func<bool> isRecording)
    {
        _settings = settings;
        _isRecording = isRecording;
        _timer = new System.Threading.Timer(Check, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
    }

    public static double GetFreeSpaceGb(string outputDirectory)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("保存先のドライブを確認できません。");
        var drive = new DriveInfo(root);
        return drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
    }

    public static void EnsureCanStart(AppSettings settings)
    {
        Directory.CreateDirectory(settings.OutputDirectory);
        var free = GetFreeSpaceGb(settings.OutputDirectory);
        if (free <= settings.AutoStopGb)
            throw new InvalidOperationException($"保存先の空き容量が {free:F1} GB しかありません。保存先を変更するか、空き容量を増やしてください。");
    }

    private void Check(object? state)
    {
        if (!_isRecording()) { _warningRaised = false; return; }
        try
        {
            var settings = _settings();
            var free = GetFreeSpaceGb(settings.OutputDirectory);
            if (free <= settings.AutoStopGb) AutoStopRequired?.Invoke(this, free);
            else if (free <= settings.LowSpaceWarningGb && !_warningRaised)
            {
                _warningRaised = true;
                LowSpace?.Invoke(this, free);
            }
        }
        catch { }
    }

    public void Dispose() => _timer.Dispose();
}
