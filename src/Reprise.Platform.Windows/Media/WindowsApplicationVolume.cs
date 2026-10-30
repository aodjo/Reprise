using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Reprise.Platform.Windows.Media;

/// <summary>Controls dedicated players through their Core Audio process sessions without touching endpoint master volume.</summary>
internal static class WindowsApplicationVolume
{
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
        Visit(appId, session =>
        {
            var volume = session.SimpleAudioVolume;
            var level = volume.Mute ? 0d : volume.Volume;
            result = Math.Max(result ?? 0, level);
        });
        return result;
    }

    /// <summary>Sets all matching streams, including an application's dedicated output device, and unmutes when raising the level.</summary>
    internal static bool Set(string appId, double value) => Visit(appId, session =>
    {
        session.SimpleAudioVolume.Volume = (float)value;
        if (value > 0) session.SimpleAudioVolume.Mute = false;
    });

    /// <summary>Reacquires sessions on an MTA worker for every operation and releases each native wrapper before returning.</summary>
    private static bool Visit(string appId, Action<AudioSessionControl> action)
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
                                action(session);
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
