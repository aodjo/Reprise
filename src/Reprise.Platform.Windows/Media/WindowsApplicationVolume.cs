using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Reprise.Platform.Windows.Media;

/// <summary>Controls dedicated players through their Core Audio process sessions without touching endpoint master volume.</summary>
internal static class WindowsApplicationVolume
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> LastOutput = new();

    /// <summary>Observes a signed player's active output across all devices, allowing short musical silences.</summary>
    internal static bool HasLocalOutput(string appId)
    {
        var found = false;
        Visit(appId, (session, process) =>
        {
            if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) return;
            var path = process.MainModule?.FileName;
            if (path is null || !WindowsPlayerTrust.IsTrusted(path, appId)) return;
            try
            {
                if (session.AudioMeterInformation.MasterPeakValue > 0.00001f) LastOutput[process.Id] = Stopwatch.GetTimestamp();
                if (LastOutput.TryGetValue(process.Id, out var last) && Stopwatch.GetElapsedTime(last) < TimeSpan.FromSeconds(8)) found = true;
            }
            catch (NotSupportedException) { }
        });
        return found;
    }
    /// <summary>Maps known dedicated media identities to executable names; browser tabs cannot safely have independent mixer volumes.</summary>
    internal static bool MatchesProcess(string appId, string processName)
    {
        if (appId.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase) ||
            appId.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase) && appId.EndsWith("!Spotify", StringComparison.OrdinalIgnoreCase))
            return processName.Equals("Spotify", StringComparison.OrdinalIgnoreCase);
        if (appId.Equals("com.kakaoent.melon.player", StringComparison.OrdinalIgnoreCase))
            return processName.Equals("Melon Player", StringComparison.OrdinalIgnoreCase) || processName.Equals("Melon", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>Reads the selected application's mixer level on all active output devices; unavailable players report no volume control.</summary>
    internal static double? Read(string appId)
    {
        double? result = null;
        Visit(appId, (session, _) =>
        {
            var volume = session.SimpleAudioVolume;
            var level = volume.Mute ? 0d : volume.Volume;
            result = Math.Max(result ?? 0, level);
        });
        return result;
    }

    /// <summary>Sets all matching streams, including an application's dedicated output device, and unmutes when raising the level.</summary>
    internal static bool Set(string appId, double value) => Visit(appId, (session, _) =>
    {
        session.SimpleAudioVolume.Volume = (float)value;
        if (value > 0) session.SimpleAudioVolume.Mute = false;
    });

    /// <summary>Reacquires sessions on an MTA worker for every operation and releases each native wrapper before returning.</summary>
    private static bool Visit(string appId, Action<AudioSessionControl, Process> action)
    {
        if (!MatchesProcess(appId, "Spotify") && !MatchesProcess(appId, "Melon Player")) return false;
        var found = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    try
                    {
                        var sessions = device.AudioSessionManager.Sessions;
                        for (var index = 0; index < sessions.Count; index++)
                        {
                            using var session = sessions[index];
                            try
                            {
                                if (session.IsSystemSoundsSession || session.GetProcessID == 0) continue;
                                using var process = Process.GetProcessById((int)session.GetProcessID);
                                if (!MatchesProcess(appId, process.ProcessName)) continue;
                                action(session, process);
                                found = true;
                            }
                            catch (Exception error) when (error is COMException or Win32Exception or ArgumentException or InvalidOperationException) { }
                        }
                    }
                    catch (COMException) { }
                }
            }
        }
        catch (COMException) { }
        return found;
    }
}
