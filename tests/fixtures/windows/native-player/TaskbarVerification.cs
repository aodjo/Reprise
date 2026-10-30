using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Reprise.Desktop;
using Reprise.Platform.Windows.Taskbar;
using Reprise.Platform.Windows.Media;

/// <summary>Verifies real taskbar alignment, cover rendering, and native lyric transition frames.</summary>
internal static class TaskbarVerification
{
    /// <summary>Exercises the actual publisher using explicitly diagnostic-only lyric lines and artwork.</summary>
    internal static async Task RunAsync(string root, bool exit)
    {
        var stage = "initial publish";
        using var publisher = new WindowsTaskbarLyricsPublisher();
        Window? button = null;
        try
        {
            await publisher.StartAsync();
            var png = File.ReadAllBytes(Path.Combine(root, "icon.png"));
            object? realArtwork = null;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                var sessions = await new WindowsMediaSessionService().GetSessionsAsync(timeout.Token);
                var session = sessions.FirstOrDefault(item => item.ArtworkUri is not null);
                if (session?.ArtworkUri is { } uri)
                {
                    var encoded = await new ArtworkLoader().LoadAsync(uri, timeout.Token);
                    if (encoded is not { Length: > 0 }) throw new InvalidOperationException("A real player's artwork URI did not load.");
                    png = TrayIconRenderer.RenderPng(encoded, 32);
                    if (png.Length == 0) throw new InvalidOperationException("A real player's artwork did not decode.");
                    realArtwork = new { session.PlayerName, session.Title, encodedBytes = encoded.Length, renderedBytes = png.Length };
                }
            }
            var state = new MenuBarState("Taskbar diagnostic", "Reprise 작업 표시줄 검사", png,
                true, true, true, 30, 20, "지금 이 순간의 노래가 작업 표시줄을 따라 부드럽게 흐릅니다", true, TrackKey: "diagnostic-track");
            Window? window = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                publisher.Publish(state);
                await Task.Delay(250);
                window = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows
                    .FirstOrDefault(item => item.Title == "Reprise 작업 표시줄 가사" && item.IsVisible);
                if (window is not null) break;
            }
            if (window is null) throw new InvalidOperationException("The taskbar strip did not appear.");
            button = new Window { Title = "Reprise taskbar collision diagnostic", Width = 320, Height = 140, ShowActivated = false };
            button.Show();
            await Task.Delay(1500);
            stage = "physical alignment";
            var frame = WindowsTaskbarGeometry.Read() ?? throw new InvalidOperationException("No taskbar frame.");
            publisher.Publish(state);
            await Task.Delay(250);
            var metrics = JsonSerializer.SerializeToElement(WindowMetrics.Read(window));
            var outer = metrics.GetProperty("Outer");
            var top = outer.GetProperty("Top").GetInt32();
            var bottom = outer.GetProperty("Bottom").GetInt32();
            if (top != frame.Bar.Top || bottom != frame.Bar.Bottom || window.IsActive || window.ShowInTaskbar ||
                metrics.GetProperty("OwnerClass").GetString() != "Shell_TrayWnd")
                throw new InvalidOperationException("Native strip bounds or activation do not match the taskbar: " + metrics);
            var viewport = window.GetVisualDescendants().OfType<TaskbarLyricViewport>().Single();
            var image = window.GetVisualDescendants().OfType<Image>().Single();
            if (image.Source is null || !image.IsEffectivelyVisible) throw new InvalidOperationException("Cover did not render.");
            stage = "sharp sentence start";
            var line = viewport.GetVisualDescendants().OfType<PanelTitleMarquee>()
                .Single(item => item.Title == state.LyricText);
            line.RestartScroll();
            var sharp = Path.Combine(root, "fade-start.png");
            var reference = Path.Combine(root, "fade-reference.png");
            Capture(window, sharp);
            line.FadesLeadingEdge = false;
            Capture(window, reference);
            line.FadesLeadingEdge = true;
            if (!File.ReadAllBytes(sharp).AsSpan().SequenceEqual(File.ReadAllBytes(reference)))
                throw new InvalidOperationException("The first letters are faded before any text is clipped.");
            Capture(window, Path.Combine(root, "taskbar-rest.png"));
            stage = "lyric push";
            var next = state with { LyricText = "다음 줄은 아래에서 부드럽게 올라옵니다" };
            publisher.Publish(next);
            var pushStarted = viewport.IsTransitioning;
            if (!pushStarted) throw new InvalidOperationException("The lyric changed without a push.");
            await Task.Delay(120);
            var layers = viewport.GetVisualDescendants().OfType<PanelTitleMarquee>()
                .Select(item => new { item.Title, item.IsVisible, Y = ((TranslateTransform)item.RenderTransform!).Y }).ToArray();
            Capture(window, Path.Combine(root, "taskbar-transition.png"));
            await Task.Delay(400);
            if (viewport.IsTransitioning || viewport.Text != next.LyricText)
                throw new InvalidOperationException("The lyric push did not finish at the current line.");
            Capture(window, Path.Combine(root, "taskbar-finished.png"));
            stage = "rapid line changes";
            publisher.Publish(next with { LyricText = "빠른 변경 첫 줄" });
            publisher.Publish(next with { LyricText = "빠른 변경 마지막 줄" });
            await Task.Delay(400);
            if (viewport.Text != "빠른 변경 마지막 줄" || viewport.IsTransitioning)
                throw new InvalidOperationException("Rapid changes retained obsolete lyrics.");
            publisher.Publish(next with { TrackKey = "different-track" });
            if (viewport.IsTransitioning) throw new InvalidOperationException("A track change animated as a lyric advance.");
            stage = "cover visibility";
            publisher.Publish(next with { ShowsArtwork = false });
            await Task.Delay(100);
            var coverHidden = !image.IsEffectivelyVisible;
            if (!coverHidden) throw new InvalidOperationException("The hidden artwork setting was ignored.");
            publisher.Publish(next with { IconPng = [1, 2, 3] });
            if (image.Source is not null) throw new InvalidOperationException("Malformed artwork retained an old cover.");
            publisher.Publish(state);
            publisher.Publish(state with { ShowsLyrics = false });
            var hiddenWhenDisabled = !window.IsVisible;
            publisher.Publish(state with { IsRunning = false });
            var hiddenWithoutPlayer = !window.IsVisible;
            if (!hiddenWhenDisabled || !hiddenWithoutPlayer) throw new InvalidOperationException("The disabled strip stayed visible.");
            publisher.Publish(state);
            stage = "unreserved compact width";
            var shortLine = state with { LyricText = "Hi", TrackKey = "compact-track", ReservesTextWidth = false };
            publisher.Publish(shortLine);
            await Task.Delay(350);
            var compact = JsonSerializer.SerializeToElement(WindowMetrics.Read(window));
            var compactOuter = compact.GetProperty("Outer");
            var expectedCompact = TaskbarLyricsLayout.Place(frame, 20, true, viewport.TextWidth, false)!.Value;
            if (compactOuter.GetProperty("Right").GetInt32() != expectedCompact.Right ||
                compactOuter.GetProperty("Right").GetInt32() - compactOuter.GetProperty("Left").GetInt32() != expectedCompact.Width)
                throw new InvalidOperationException("Native compact width did not match shaped text: " + compact);
            stage = "native animated width";
            publisher.Publish(shortLine with { LyricText = state.LyricText });
            await Task.Delay(100);
            var growing = JsonSerializer.SerializeToElement(WindowMetrics.Read(window));
            await Task.Delay(300);
            var grown = JsonSerializer.SerializeToElement(WindowMetrics.Read(window));
            var growingWidth = growing.GetProperty("Outer").GetProperty("Right").GetInt32()
                - growing.GetProperty("Outer").GetProperty("Left").GetInt32();
            var grownWidth = grown.GetProperty("Outer").GetProperty("Right").GetInt32()
                - grown.GetProperty("Outer").GetProperty("Left").GetInt32();
            if (growingWidth <= expectedCompact.Width || growingWidth >= grownWidth)
                throw new InvalidOperationException("Native width jumped instead of animating: " + growing + "; final: " + grown);
            publisher.Publish(shortLine with { ReservesTextWidth = true });
            await Task.Delay(350);
            var reserved = JsonSerializer.SerializeToElement(WindowMetrics.Read(window));
            var reservedOuter = reserved.GetProperty("Outer");
            if (reservedOuter.GetProperty("Right").GetInt32() - reservedOuter.GetProperty("Left").GetInt32() <= expectedCompact.Width)
                throw new InvalidOperationException("Reservation did not retain the maximum width.");
            stage = "left taskbar placement";
            publisher.Publish(shortLine with { PrefersLeft = true });
            await Task.Delay(350);
            frame = WindowsTaskbarGeometry.Read() ?? throw new InvalidOperationException("No taskbar geometry for left placement.");
            var left = JsonSerializer.SerializeToElement(WindowMetrics.Read(window));
            var expectedLeft = TaskbarLyricsLayout.Place(frame, 20, true, viewport.TextWidth, false, true)!.Value;
            if (left.GetProperty("Outer").GetProperty("Left").GetInt32() != expectedLeft.Left ||
                left.GetProperty("Outer").GetProperty("Right").GetInt32() != expectedLeft.Right)
                throw new InvalidOperationException("The left placement setting did not reach the native window: " + left);
            publisher.Publish(state);
            stage = "native strip recreation";
            window.Close();
            await Task.Delay(600);
            var replacement = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows
                .SingleOrDefault(item => item.Title == "Reprise 작업 표시줄 가사" && item.IsVisible);
            if (replacement is null || ReferenceEquals(window, replacement) || replacement.IsActive ||
                replacement.GetVisualDescendants().OfType<TaskbarLyricViewport>().Single().Text != state.LyricText)
                throw new InvalidOperationException("The native strip did not recover its lyric state after closure.");
            window = replacement;
            File.WriteAllText(Path.Combine(root, "taskbar-result.json"), JsonSerializer.Serialize(new
            {
                ok = true, Frame = frame, Native = metrics, Layers = layers, pushStarted, coverHidden, realArtwork,
                sentenceStartMatchesUnfadedReference = true,
                nativeStripRecreated = true,
                Compact = compact, Reserved = reserved, Growing = growing, Grown = grown, Left = left,
                leftGapUsed = frame.LeadingSpace is { } gap && expectedLeft.Left >= gap.Left && expectedLeft.Right <= gap.Right,
                hiddenWhenDisabled, hiddenWithoutPlayer, Placement = TaskbarLyricsLayout.Place(frame, 20, true),
                Children = WindowsTaskbarGeometry.Diagnose(),
            }));
            button.Close();
            while (!exit && !File.Exists(Path.Combine(root, "stop"))) await Task.Delay(250);
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(root, "taskbar-result.json"), JsonSerializer.Serialize(new { ok = false, stage, error = error.ToString() }));
        }
        finally { button?.Close(); }
    }

    /// <summary>Renders only the diagnostic's own window; this is not a desktop screenshot.</summary>
    private static void Capture(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
