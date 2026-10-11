using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LumaCapture.Infrastructure;

public sealed class InstallerService
{
    public string InstallDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LumaCapture");
    public string InstalledExecutable => Path.Combine(InstallDirectory, "LumaCapture.exe");
    public string InstalledFfmpeg => Path.Combine(InstallDirectory, "ffmpeg.exe");
    private string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "LumaCapture.lnk");
    private string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "LumaCapture.lnk");
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LumaCapture";

    public static bool IsSetupExecutableName(string? path) => Path.GetFileNameWithoutExtension(path)?.Contains("Setup", StringComparison.OrdinalIgnoreCase) == true;

    public async Task InstallAsync(bool desktopShortcut, bool startup, IProgress<double>? progress = null)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("インストーラーの場所を確認できません。");
        Directory.CreateDirectory(InstallDirectory);
        progress?.Report(0.05);
        await CopyWithProgressAsync(source, InstalledExecutable, value => progress?.Report(0.05 + value * 0.55));
        progress?.Report(0.62);
        var stagedFfmpeg = InstalledFfmpeg + ".new";
        if (!await Task.Run(() => FfmpegService.ExtractEmbeddedExecutable(stagedFfmpeg)))
        {
            var adjacent = Path.Combine(Path.GetDirectoryName(source)!, "ffmpeg.exe");
            if (File.Exists(adjacent)) await CopyWithProgressAsync(adjacent, stagedFfmpeg, value => progress?.Report(0.62 + value * 0.25));
            else throw new FileNotFoundException("録画エンジン ffmpeg.exe がインストーラーに含まれていません。");
        }
        File.Move(stagedFfmpeg, InstalledFfmpeg, true);
        foreach (var name in new[] { "FFmpeg-LICENSE.txt", "THIRD-PARTY-NOTICES.md" })
        {
            using var resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("LumaCapture." + name);
            if (resource is null) continue;
            using var destination = File.Create(Path.Combine(InstallDirectory, name));
            resource.CopyTo(destination);
        }
        progress?.Report(0.9);
        CreateShortcut(StartMenuShortcut, InstalledExecutable);
        if (desktopShortcut) CreateShortcut(DesktopShortcut, InstalledExecutable); else TryDelete(DesktopShortcut);
        StartupService.Apply(startup, InstalledExecutable);
        RegisterUninstaller();
        progress?.Report(1);
    }

    private static async Task CopyWithProgressAsync(string source, string destination, Action<double> progress)
    {
        var temp = destination + ".new";
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
        await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            var buffer = new byte[1024 * 1024]; int read; long copied = 0;
            while ((read = await input.ReadAsync(buffer)) > 0) { await output.WriteAsync(buffer.AsMemory(0, read)); copied += read; progress(input.Length == 0 ? 1 : (double)copied / input.Length); }
        }
        File.Move(temp, destination, true);
    }

    private void RegisterUninstaller()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "LumaCapture"); key.SetValue("DisplayVersion", "0.12.1"); key.SetValue("Publisher", "LumaCapture"); key.SetValue("InstallLocation", InstallDirectory); key.SetValue("DisplayIcon", InstalledExecutable); key.SetValue("UninstallString", $"\"{InstalledExecutable}\" --uninstall"); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void CreateShortcut(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("ショートカットを作成できません。");
        dynamic shell = Activator.CreateInstance(shellType)!; dynamic shortcut = shell.CreateShortcut(path); shortcut.TargetPath = target; shortcut.WorkingDirectory = Path.GetDirectoryName(target); shortcut.Description = "LumaCapture を起動"; shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);
    }

    public string CreateTemporaryUninstaller()
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException();
        var target = Path.Combine(Path.GetTempPath(), "LumaCapture-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(source, target, true); return target;
    }

    public async Task FinishUninstallAsync(string targetDirectory, IProgress<double>? progress = null)
    {
        var fullTarget = Path.GetFullPath(targetDirectory);
        if (!fullTarget.Equals(Path.GetFullPath(InstallDirectory), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("インストール先以外は削除できません。");
        foreach (var process in Process.GetProcessesByName("LumaCapture"))
        {
            using (process) { if (process.Id == Environment.ProcessId) continue; try { process.Kill(true); await process.WaitForExitAsync(); } catch { } }
        }
        progress?.Report(0.25); StartupService.Apply(false, InstalledExecutable); TryDelete(StartMenuShortcut); TryDelete(DesktopShortcut); Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); progress?.Report(0.55);
        for (var attempt = 0; attempt < 8 && Directory.Exists(fullTarget); attempt++) { try { Directory.Delete(fullTarget, true); } catch when (attempt < 7) { await Task.Delay(350); } }
        progress?.Report(1);
        if (Environment.ProcessPath is { } temporary) MoveFileEx(temporary, null, 4);
    }

    public void StartInstalledApp() => Process.Start(new ProcessStartInfo(InstalledExecutable) { UseShellExecute = true });
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool MoveFileEx(string existing, string? destination, int flags);
}
