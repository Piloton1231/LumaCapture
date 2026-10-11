using System.Diagnostics;
using LumaCapture.Models;
using NAudio.CoreAudioApi;

namespace LumaCapture.Infrastructure;

/// <summary>
/// Reads the audio-engine peak meters without opening another capture stream.
/// This keeps the settings preview lightweight and avoids competing with an
/// active recording or clip buffer.
/// </summary>
public sealed class AudioLevelMonitor : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _renderDevice;
    private AudioSessionManager? _sessionManager;
    private MMDevice? _microphoneDevice;
    private string _microphoneDeviceId = string.Empty;
    private string _processSelectionKey = string.Empty;
    private HashSet<uint> _selectedProcessIds = new();
    private DateTime _processIdsRefreshedUtc = DateTime.MinValue;
    private bool _disposed;

    public AudioLevels Read(AudioCaptureMode mode, IEnumerable<string> selectedApplications, string microphoneDeviceId)
    {
        if (_disposed) return default;
        var audio = mode == AudioCaptureMode.SystemAudio
            ? ReadSystemOutputLevel()
            : ReadSelectedApplicationLevel(selectedApplications);
        var microphone = string.IsNullOrWhiteSpace(microphoneDeviceId) ? 0f : ReadMicrophoneLevel(microphoneDeviceId);
        return new AudioLevels(Clamp(audio), Clamp(microphone));
    }

    private float ReadSystemOutputLevel()
    {
        try
        {
            EnsureRenderDevice();
            return _renderDevice?.AudioMeterInformation.MasterPeakValue ?? 0f;
        }
        catch
        {
            ResetRenderDevice();
            return 0f;
        }
    }

    private float ReadSelectedApplicationLevel(IEnumerable<string> selectedApplications)
    {
        try
        {
            EnsureRenderDevice();
            RefreshSelectedProcessIds(selectedApplications);
            if (_sessionManager is null || _selectedProcessIds.Count == 0) return 0f;

            var peak = 0f;
            var sessions = _sessionManager.Sessions;
            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                if (!_selectedProcessIds.Contains(session.GetProcessID)) continue;
                using var meter = session.AudioMeterInformation;
                peak = Math.Max(peak, meter.MasterPeakValue);
            }
            return peak;
        }
        catch
        {
            ResetRenderDevice();
            return 0f;
        }
    }

    private float ReadMicrophoneLevel(string deviceId)
    {
        try
        {
            if (_microphoneDevice is null || !string.Equals(deviceId, _microphoneDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                _microphoneDevice?.Dispose();
                _microphoneDevice = _enumerator.GetDevice(deviceId);
                _microphoneDeviceId = deviceId;
            }
            return _microphoneDevice.AudioMeterInformation.MasterPeakValue;
        }
        catch
        {
            _microphoneDevice?.Dispose();
            _microphoneDevice = null;
            _microphoneDeviceId = string.Empty;
            return 0f;
        }
    }

    private void EnsureRenderDevice()
    {
        if (_renderDevice is not null && _sessionManager is not null) return;
        ResetRenderDevice();
        _renderDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _sessionManager = _renderDevice.AudioSessionManager;
    }

    private void RefreshSelectedProcessIds(IEnumerable<string> selectedApplications)
    {
        var names = selectedApplications.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var key = string.Join('\n', names);
        var now = DateTime.UtcNow;
        if (string.Equals(key, _processSelectionKey, StringComparison.Ordinal) && now - _processIdsRefreshedUtc < TimeSpan.FromSeconds(1)) return;

        _processSelectionKey = key;
        _processIdsRefreshedUtc = now;
        var selected = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcesses())
        {
            try { if (selected.Contains(process.ProcessName)) processIds.Add((uint)process.Id); }
            catch { }
            finally { process.Dispose(); }
        }
        _selectedProcessIds = processIds;
    }

    private void ResetRenderDevice()
    {
        _sessionManager?.Dispose();
        _sessionManager = null;
        _renderDevice?.Dispose();
        _renderDevice = null;
    }

    private static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetRenderDevice();
        _microphoneDevice?.Dispose();
        _microphoneDevice = null;
        _enumerator.Dispose();
    }
}

public readonly record struct AudioLevels(float Audio, float Microphone);
