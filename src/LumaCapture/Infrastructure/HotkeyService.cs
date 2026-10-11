using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public sealed class HotkeyService : IDisposable
{
    private const int CaptureId = 0x4C01;
    private const int ClipId = 0x4C02;
    private readonly HwndSource _source;
    public event EventHandler? CapturePressed;
    public event EventHandler? ClipPressed;

    public HotkeyService(Dispatcher dispatcher)
    {
        if (!dispatcher.CheckAccess()) throw new InvalidOperationException("Hotkeys must be created on the UI thread.");
        _source = new HwndSource(new HwndSourceParameters("LumaCapture.Hotkeys")
        {
            Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3)
        });
        _source.AddHook(WndProc);
    }

    public HotkeyRegistration Configure(AppSettings settings)
    {
        Stop();
        var capture = Register(CaptureId, settings.CaptureHotkey);
        var clip = Register(ClipId, settings.ClipHotkey);
        return new HotkeyRegistration(capture, clip);
    }

    private bool Register(int id, HotkeyGesture gesture)
    {
        if (!Enum.TryParse<Key>(gesture.Key, true, out var key)) return false;
        var modifiers = 0x4000u;
        if (gesture.Alt) modifiers |= NativeMethods.ModAlt;
        if (gesture.Control) modifiers |= NativeMethods.ModControl;
        if (gesture.Shift) modifiers |= NativeMethods.ModShift;
        if (gesture.Windows) modifiers |= NativeMethods.ModWin;
        return NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotkey) return IntPtr.Zero;
        handled = true;
        switch (wParam.ToInt32())
        {
            case CaptureId: CapturePressed?.Invoke(this, EventArgs.Empty); break;
            case ClipId: ClipPressed?.Invoke(this, EventArgs.Empty); break;
        }
        return IntPtr.Zero;
    }

    public void Stop()
    {
        NativeMethods.UnregisterHotKey(_source.Handle, CaptureId);
        NativeMethods.UnregisterHotKey(_source.Handle, ClipId);
    }

    public void Dispose()
    {
        Stop();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
