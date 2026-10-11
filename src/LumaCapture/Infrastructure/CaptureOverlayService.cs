using LumaCapture.Models;
using LumaCapture.Windows;

namespace LumaCapture.Infrastructure;

public sealed class CaptureOverlayService : IDisposable
{
    private readonly List<CaptureTargetOverlayWindow> _windows = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private AppSettings? _settings;

    public CaptureOverlayService()
    {
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Show(AppSettings settings)
    {
        _settings = settings.Clone();
        Refresh();
        _timer.Start();
    }

    private void Refresh()
    {
        if (_settings is null) return;
        IReadOnlyList<ResolvedCaptureTarget> targets;
        try { targets = CaptureTargetResolver.Resolve(_settings); }
        catch { Hide(); return; }
        while (_windows.Count < targets.Count) _windows.Add(new CaptureTargetOverlayWindow());
        for (var index = 0; index < targets.Count; index++) _windows[index].ShowBounds(targets[index].Bounds);
        for (var index = targets.Count; index < _windows.Count; index++) _windows[index].Hide();
    }

    public void Hide()
    {
        _timer.Stop();
        _settings = null;
        foreach (var window in _windows) window.Hide();
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var window in _windows) window.Close();
        _windows.Clear();
    }
}
