using System.Text.Json;
using Windows.Media.Control;
using Reprise.Core;
using Reprise.Platform.Windows.Media;
using Reprise.Platform.Windows.Taskbar;
using NAudio.CoreAudioApi;
using System.Diagnostics;

/// <summary>Exercises actual installed players rather than fixture metadata, restoring their original playback and volume.</summary>
internal static class PlayerControlVerification
{
    /// <summary>Records current broker capabilities and taskbar geometry without changing a player's state.</summary>
    internal static async Task ObserveAsync(string root)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var service = new WindowsMediaSessionService();
        var sessions = await service.GetSessionsAsync();
        var controls = manager.GetSessions().Select(session =>
        {
            var playback = session.GetPlaybackInfo();
            var c = playback.Controls;
            return new { appId = session.SourceAppUserModelId, playback.PlaybackStatus,
                c.IsPlayEnabled, c.IsPauseEnabled, c.IsPlayPauseToggleEnabled, c.IsStopEnabled };
        }).ToArray();
        TaskbarFrame? frame = null;
        for (var attempt = 0; attempt < 20 && frame is null; attempt++)
        {
            frame = WindowsTaskbarGeometry.Read();
            await Task.Delay(250);
        }
        var audio = await Task.Run(ObserveAudio);
        sessions = await service.GetSessionsAsync();
        File.WriteAllText(Path.Combine(root, "players-observation.json"), JsonSerializer.Serialize(new
            { sessions, controls, audio, spotify = new { SpotifyAccountConnection.Shared.IsConfigured, SpotifyAccountConnection.Shared.IsConnected, SpotifyAccountConnection.Shared.Status },
                frame, elements = WindowsTaskbarGeometry.DiagnoseAccessibility() }));
    }

    /// <summary>Records audio endpoints and application process identities to distinguish unavailable local streams from mapping failures.</summary>
    private static object ObserveAudio()
    {
        var result = new List<object>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    var streams = new List<object>();
                    for (var index = 0; index < sessions.Count; index++)
                    {
                        using var session = sessions[index];
                        var pid = session.GetProcessID;
                        string? name = null;
                        try { if (pid != 0) { using var process = Process.GetProcessById((int)pid); name = process.ProcessName; } }
                        catch (ArgumentException) { }
                        streams.Add(new { pid, name, session.State, volume = session.SimpleAudioVolume.Volume, muted = session.SimpleAudioVolume.Mute });
                    }
                    result.Add(new { device.FriendlyName, streams });
                }
            }
        }
        catch (Exception error) { result.Add(new { error = error.ToString() }); }
        return result;
    }

    /// <summary>Changes a real selected application and observes the broker/mixer acknowledgement before restoring its prior state.</summary>
    internal static async Task VerifyAsync(string root, string player, bool volume)
    {
        var service = new WindowsMediaSessionService();
        MediaSessionSnapshot? initial = null;
        var restorePlayback = false;
        var stage = "locate player";
        var ok = false;
        var restored = false;
        string? failure = null;
        double? targetVolume = null;
        MediaSessionSnapshot? changed = null;
        try
        {
            initial = (await service.GetSessionsAsync()).Single(session => session.PlayerId.Contains(player, StringComparison.OrdinalIgnoreCase));
            if (volume)
            {
                stage = "change selected player volume";
                for (var attempt = 0; attempt < 20 && initial.Volume is null; attempt++)
                {
                    await Task.Delay(250);
                    initial = (await service.GetSessionsAsync()).Single(session => session.PlayerId == initial.PlayerId);
                }
                if (initial.Volume is null) throw new InvalidOperationException("No real player volume control is available.");
                var target = initial.Volume > 0.6 ? 0.35 : 0.75;
                targetVolume = target;
                await service.SetVolumeAsync(initial.PlayerId, target);
                changed = await WaitAsync(service, initial.PlayerId, session => session.Volume is { } level && Math.Abs(level - target) < 0.015);
            }
            else
            {
                restorePlayback = true;
                if (initial.Status != PlaybackStatus.Playing)
                {
                    stage = "start selected player";
                    await service.SendCommandAsync(initial.PlayerId, PlaybackCommand.PlayPause);
                    await WaitAsync(service, initial.PlayerId, session => session.Status == PlaybackStatus.Playing);
                }
                stage = "pause selected player";
                await service.SendCommandAsync(initial.PlayerId, PlaybackCommand.PlayPause);
                changed = await WaitAsync(service, initial.PlayerId, session => session.Status == PlaybackStatus.Paused);
            }
            ok = true;
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            failure = error.ToString();
        }
        finally
        {
            try
            {
                if (initial is not null)
                {
                    if (volume && initial.Volume is { } level)
                    {
                        await service.SetVolumeAsync(initial.PlayerId, level);
                        await WaitAsync(service, initial.PlayerId, session => session.Volume is { } value && Math.Abs(value - level) < 0.015);
                    }
                    if (restorePlayback)
                    {
                        var current = (await service.GetSessionsAsync()).SingleOrDefault(session => session.PlayerId == initial.PlayerId);
                        if (initial.Status == PlaybackStatus.Playing && current?.Status != PlaybackStatus.Playing)
                            await service.SendCommandAsync(initial.PlayerId, PlaybackCommand.PlayPause);
                        else if (initial.Status != PlaybackStatus.Playing && current?.Status == PlaybackStatus.Playing)
                            await service.SendCommandAsync(initial.PlayerId, PlaybackCommand.Pause);
                        await WaitAsync(service, initial.PlayerId, session => (session.Status == PlaybackStatus.Playing) == (initial.Status == PlaybackStatus.Playing));
                    }
                }
                restored = true;
            }
            catch (Exception error) { Environment.ExitCode = 1; failure += "\nRestore: " + error; }
            File.WriteAllText(Path.Combine(root, $"player-{player}-{(volume ? "volume" : "control")}.json"),
                JsonSerializer.Serialize(new { ok = ok && restored, stage, initial, targetVolume, changed, restored, error = failure }));
        }
    }

    /// <summary>Requires an actual observed player change; a successful request return value alone is insufficient.</summary>
    private static async Task<MediaSessionSnapshot> WaitAsync(WindowsMediaSessionService service, string playerId, Func<MediaSessionSnapshot, bool> condition)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var session = (await service.GetSessionsAsync()).SingleOrDefault(item => item.PlayerId == playerId);
            if (session is not null && condition(session)) return session;
            await Task.Delay(250);
        }
        throw new InvalidOperationException("The player did not acknowledge the requested state change.");
    }
}
