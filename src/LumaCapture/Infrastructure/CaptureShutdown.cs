using System.Diagnostics;
using System.Collections.Concurrent;

namespace LumaCapture.Infrastructure;

// Only processes created by this instance are registered. Never kill by name:
// other software may also use FFmpeg.
internal static class CaptureShutdown
{
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<int, (Process Process, Action RequestStop)> Children = new();
    public static bool IsEnding { get; private set; }
    internal static int ActiveChildren => Children.Values.Count(child => { try { return !child.Process.HasExited; } catch { return false; } });
    public static void ConfigureCurrentProcess() => SetProcessShutdownParameters(0x380, 0);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessShutdownParameters(uint level, uint flags);

    public static Process Start(ProcessStartInfo info, Action<Process> requestStop)
    {
        lock (Gate)
        {
            if (IsEnding) throw new OperationCanceledException("Windows session is ending.");
            var process = Process.Start(info) ?? throw new IOException("Cannot start capture engine.");
            Children[process.Id] = (process, () => requestStop(process));
            return process;
        }
    }
    public static void Forget(Process process) { try { Children.TryRemove(process.Id, out _); } catch { } }
    public static void Begin() { lock (Gate) IsEnding = true; }
    public static async Task StopAsync()
    {
        Begin();
        var children = Children.Values.ToArray();
        await Task.WhenAll(children.Select(async child =>
        {
            try
            {
                if (child.Process.HasExited) return;
                // Pipe writes may be blocked; they must not block session ending.
                _ = Task.Run(() => { try { child.RequestStop(); } catch { } });
                try { await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (TimeoutException) { try { child.Process.Kill(true); await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false); } catch { } }
            }
            catch (InvalidOperationException) { }
        })).ConfigureAwait(false);
    }
}
