using System.Text.Json;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Reprise.Core;
using Reprise.Platform.Windows.Media;
using Reprise.Platform.Windows.Taskbar;
using Reprise.Desktop;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

/// <summary>Runs the native provider and consumer on the same pumped STA as a real desktop app.</summary>
internal static class Program
{
    /// <summary>Starts the native Windows dispatcher used by WinRT media components.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--verify-update", var metadata, var installer])
        {
            UpdateHandoff.Verify(metadata, installer);
            return 0;
        }
        return AppBuilder.Configure<FixtureApplication>()
            .With(new Win32PlatformOptions { WinUICompositionBackdropCornerRadius = (float)NowPlayingWindow.PanelCornerRadius })
            .UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Exercises an actual media session in an interactive user desktop.</summary>
    internal static async Task RunAsync(string[] args)
    {
        var root = AppContext.BaseDirectory;
        if (args.Contains("--observe-players"))
        {
            await PlayerControlVerification.ObserveAsync(root);
            return;
        }
        if (args is ["--verify-player-control", var selectedPlayer])
        {
            await PlayerControlVerification.VerifyAsync(root, selectedPlayer, false);
            return;
        }
        if (args is ["--verify-player-volume", var volumePlayer])
        {
            await PlayerControlVerification.VerifyAsync(root, volumePlayer, true);
            return;
        }
        if (args.Contains("--fullscreen-target"))
        {
            await TaskbarVisibilityVerification.RunFullscreenTargetAsync(root);
            return;
        }
        if (args.Contains("--verify-taskbar-fullscreen"))
        {
            await TaskbarVisibilityVerification.VerifyFullscreenAsync(root);
            return;
        }
        if (args.Contains("--observe-taskbar") || args.Contains("--watch-taskbar"))
        {
            await TaskbarVisibilityVerification.ObserveAsync(root, args.Contains("--watch-taskbar"));
            return;
        }
        if (args.Contains("--verify-lyrics-highlight"))
        {
            await LyricsHighlightVerification.RunAsync(root);
            return;
        }
        if (args.Contains("--verify-ui"))
        {
            await MaterialVerification.RunAsync(root);
            await FadeVerification.RunAsync(root);
            await LyricsHighlightVerification.RunAsync(root);
            await SettingsVerification.RunAsync(root);
            return;
        }
        if (args.Contains("--verify-fade"))
        {
            await FadeVerification.RunAsync(root);
            return;
        }
        if (args.Contains("--verify-material"))
        {
            await MaterialVerification.RunAsync(root);
            return;
        }
        if (args.Contains("--verify-settings"))
        {
            await SettingsVerification.RunAsync(root);
            return;
        }
        if (args.Contains("--taskbar-lyrics"))
        {
            await TaskbarVerification.RunAsync(root, args.Contains("--verify-and-exit"));
            return;
        }
        var log = Path.Combine(root, "result.json");
        Console.SetError(TextWriter.Synchronized(new StreamWriter(Path.Combine(root, "errors.log")) { AutoFlush = true }));
        var events = new List<string>();
        var stage = "initialization";
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Generate test-only silence, never expose fixture data through the production backend.
            using (var output = new BinaryWriter(File.Create(Path.Combine(root, "silence.wav"))))
            {
                const int bytes = 120 * 8000 * 2;
                output.Write("RIFF"u8); output.Write(bytes + 36); output.Write("WAVEfmt "u8);
                output.Write(16); output.Write((short)1); output.Write((short)1);
                output.Write(8000); output.Write(16000); output.Write((short)2); output.Write((short)16);
                output.Write("data"u8); output.Write(bytes); output.Write(new byte[bytes]);
            }
            using var player = new MediaPlayer();
            player.MediaFailed += (_, failure) => { lock(events) events.Add("FAILED: " + failure.Error + ": " + failure.ErrorMessage + ": " + failure.ExtendedErrorCode); };
            player.MediaEnded += (_, _) => { lock(events) events.Add("ENDED"); };
            player.PlaybackSession.PlaybackStateChanged += (_, _) => { lock(events) events.Add(player.PlaybackSession.PlaybackState.ToString()); };
            player.IsLoopingEnabled = true;
            player.Volume = 1;
            player.CommandManager.IsEnabled = false;
            var controls = player.SystemMediaTransportControls;
            controls.IsEnabled = true;
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsNextEnabled = true;
            controls.IsPreviousEnabled = true;
            controls.DisplayUpdater.Type = MediaPlaybackType.Music;
            controls.DisplayUpdater.MusicProperties.Title = "Reprise Windows native playback verification — smooth scrolling title";
            controls.DisplayUpdater.MusicProperties.Artist = "Reprise integration fixture";
            controls.DisplayUpdater.MusicProperties.AlbumTitle = "Windows platform test";
            controls.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(root, "icon.png")));
            controls.DisplayUpdater.Update();
            /// <summary>Publish the fixture's actual playback position through native SMTC.</summary>
            void Timeline()
            {
                controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(120),
                    MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(120),
                    Position = player.PlaybackSession.Position,
                });
            }
            controls.ButtonPressed += (_, args) =>
            {
                lock (events) events.Add(args.Button.ToString());
                if (args.Button == SystemMediaTransportControlsButton.Pause) { player.Pause(); controls.PlaybackStatus = MediaPlaybackStatus.Paused; }
                if (args.Button == SystemMediaTransportControlsButton.Play) { player.Play(); controls.PlaybackStatus = MediaPlaybackStatus.Playing; }
                Timeline();
            };
            controls.PlaybackPositionChangeRequested += (_, args) =>
            {
                lock (events) events.Add("Seek");
                player.PlaybackSession.Position = args.RequestedPlaybackPosition;
                Timeline();
            };
            player.Source = MediaSource.CreateFromUri(new Uri(Path.Combine(root, "silence.wav")));
            player.Play();
            controls.PlaybackStatus = MediaPlaybackStatus.Playing;
            await Task.Delay(1500);
            Timeline();
            var service = new WindowsMediaSessionService();
            MediaSessionSnapshot? session = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                session = (await service.GetSessionsAsync()).FirstOrDefault(x => x.Title.StartsWith("Reprise Windows native"));
                if (session != null) break;
                await Task.Delay(500);
            }
            if (session == null) throw new Exception("Native fixture session was not discovered.");
            stage = "identity across polls: " + session.PlayerId;
            var again = await service.GetSessionsAsync();
            var same = again.FirstOrDefault(x => x.PlayerId == session.PlayerId)
                ?? throw new Exception("Initial: " + JsonSerializer.Serialize(session) + "; Next: " + JsonSerializer.Serialize(again));
            if (same.ArtworkUri == null || !File.Exists(same.ArtworkUri.LocalPath)) throw new Exception("Artwork was not read: " + JsonSerializer.Serialize(same));
            stage = "sending pause";
            await service.SendCommandAsync(session.PlayerId, PlaybackCommand.Pause);
            await Task.Delay(500);
            stage = "observing pause";
            var paused = (await service.GetSessionsAsync()).Single(x => x.PlayerId == session.PlayerId);
            if (paused.Status != Reprise.Core.PlaybackStatus.Paused) throw new Exception("Pause was not observed.");
            await service.SendCommandAsync(session.PlayerId, PlaybackCommand.PlayPause);
            await Task.Delay(500);
            stage = "observing play";
            var playing = (await service.GetSessionsAsync()).Single(x => x.PlayerId == session.PlayerId);
            if (playing.Status != Reprise.Core.PlaybackStatus.Playing) throw new Exception("Play was not observed.");
            await service.SeekAsync(session.PlayerId, TimeSpan.FromSeconds(30));
            await Task.Delay(500);
            stage = "observing seek";
            var sought = (await service.GetSessionsAsync()).Single(x => x.PlayerId == session.PlayerId);
            if (sought.Position?.TotalSeconds is not (>=29 and <40)) throw new Exception("Seek was not observed: " + sought.Position);
            await service.SendCommandAsync(session.PlayerId, PlaybackCommand.Next);
            await service.SendCommandAsync(session.PlayerId, PlaybackCommand.Previous);
        await Task.Delay(250);
            File.WriteAllText(log, JsonSerializer.Serialize(new { ok=true, session, paused, playing, sought, events }));
            while (!args.Contains("--verify-and-exit") && !File.Exists(Path.Combine(root, "stop"))) { Timeline(); await Task.Delay(1000); }
        }
        catch (Exception error)
        {
            File.WriteAllText(log, JsonSerializer.Serialize(new { ok=false, stage, seconds=elapsed.Elapsed.TotalSeconds, events, error=error.ToString() }));
            Environment.ExitCode = 1;
        }
    }
}

/// <summary>Owns the message pump required by the test media provider.</summary>
internal sealed class FixtureApplication : Application
{
    /// <summary>Uses the same control theme as the production application.</summary>
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Run(desktop));
        }
    }

    /// <summary>Ends the diagnostic lifetime when verification finishes or the stop file appears.</summary>
    private static async void Run(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try { await Program.RunAsync(desktop.Args ?? []); }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fixture-error.json"), JsonSerializer.Serialize(new { error = error.ToString() }));
            Environment.ExitCode = 1;
        }
        desktop.Shutdown(Environment.ExitCode);
    }
}
