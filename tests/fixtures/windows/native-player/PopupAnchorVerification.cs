using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Reprise.Desktop;
using Reprise.Platform.Windows.Media;
using Reprise.Platform.Windows.Taskbar;

/// <summary>Checks native popup positioning through actual widget presses and the shared activation controller.</summary>
internal static class PopupAnchorVerification
{
    /// <summary>Exercises both placements, animated bounds, layout resizing, reopening, and screen-edge clamping.</summary>
    internal static async Task RunAsync(string root)
    {
        var stage = "construct";
        var results = new List<object>();
        var preferences = new PreferencesStore(Path.Combine(root, "popup-preferences.json"));
        preferences.Update(current => current with { AutomaticallyPausesOtherPlayer = false });
        using var model = new NowPlayingViewModel(new WindowsMediaSessionService(), preferences: () => preferences.Current);
        using var statusModel = new NowPlayingViewModel(new WindowsMediaSessionService(), preferences: () => preferences.Current);
        var panel = new NowPlayingWindow(model, preferences);
        using var publisher = new WindowsTaskbarLyricsPublisher();
        using var controller = new StatusItemController(new AvaloniaTrayStatusItem(Application.Current!), statusModel, preferences, publisher);
        MenuBarActivatedEventArgs? captured = null;
        controller.Activated += (_, args) =>
        {
            captured = args as MenuBarActivatedEventArgs;
            panel.TogglePanel(captured?.Bounds);
        };
        try
        {
            await publisher.StartAsync();
            var state = new MenuBarState("Popup alignment", "Reprise popup diagnostic", [], true, true, true, 30, 40,
                "위젯 시작점 확인", true, false, "popup-diagnostic");
            foreach (var left in new[] { false, true })
            {
                stage = left ? "left widget" : "right widget";
                Window? widget = null;
                for (var attempt = 0; attempt < 40 && widget is null; attempt++)
                {
                    publisher.Publish(state with { PrefersLeft = left });
                    await Task.Delay(250);
                    widget = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows
                        .SingleOrDefault(item => item.Title == "Reprise 작업 표시줄 가사" && item.IsVisible);
                }
                if (widget is null) throw new InvalidOperationException("No visible widget: " + JsonSerializer.Serialize(WindowsTaskbarGeometry.Read()));
                var widgetBounds = Bounds(widget);
                Press(widget);
                await Task.Delay(450);
                if (captured?.Bounds.X != widgetBounds.X || captured.Bounds.Width != widgetBounds.Width)
                    throw new InvalidOperationException("The controller lost the widget's physical activation bounds.");
                results.Add(Check(panel, captured.Bounds, stage));
                ((Control)panel.Content!).MinHeight = panel.Bounds.Height + 40;
                await Task.Delay(300);
                results.Add(Check(panel, captured.Bounds, stage + " resized"));
                panel.HidePanel();
                stage += " animated";
                publisher.Publish(state with { PrefersLeft = left, LyricText = "가사 길이가 달라져 너비가 바뀌는 동안에도 클릭한 위젯의 시작점을 기준으로 표시합니다" });
                await Task.Delay(80);
                widgetBounds = Bounds(widget);
                Press(widget);
                await Task.Delay(450);
                if (captured?.Bounds.X != widgetBounds.X)
                    throw new InvalidOperationException("Activation used a target rectangle instead of the visible animation frame.");
                results.Add(Check(panel, captured.Bounds, stage));
                panel.HidePanel();
            }
            var screen = panel.Screens.Primary!;
            foreach (var x in new[] { screen.Bounds.X, screen.Bounds.Right - 1 })
            {
                stage = "edge clamp " + x;
                var anchor = new PixelRect(x, screen.Bounds.Bottom - 1, 1, 1);
                panel.ShowPanel(anchor);
                await Task.Delay(300);
                results.Add(Check(panel, anchor, stage));
                panel.HidePanel();
            }
            stage = "unanchored reopen";
            panel.ShowPanel();
            await Task.Delay(300);
            var area = screen.WorkingArea;
            var actual = Bounds(panel);
            // The default right-corner fallback must not inherit a previous widget click.
            if (actual.X <= area.X || actual.Right > area.Right)
                throw new InvalidOperationException("Unanchored activation retained a stale widget anchor.");
            File.WriteAllText(Path.Combine(root, "popup-result.json"), JsonSerializer.Serialize(new { ok = true, results, unanchored = actual }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "popup-result.json"), JsonSerializer.Serialize(new { ok = false, stage, results, error = error.ToString() }));
            Environment.ExitCode = 1;
        }
        finally { panel.AllowClose = true; panel.Close(); }
    }

    /// <summary>Compares independently measured native popup geometry with the clicked widget and monitor work area.</summary>
    private static object Check(NowPlayingWindow panel, PixelRect anchor, string stage)
    {
        var actual = Bounds(panel);
        var screen = panel.Screens.ScreenFromPoint(new PixelPoint(anchor.X, anchor.Y))!;
        var area = screen.WorkingArea;
        var expected = Math.Clamp(anchor.X, area.X, Math.Max(area.X, area.Right - actual.Width));
        if (!panel.IsVisible || actual.X != expected || actual.Bottom > area.Bottom)
            throw new InvalidOperationException($"Popup alignment mismatch at {stage}: {actual}, expected X {expected}, area {area}.");
        return new { stage, anchor, actual, expected, area, screen.Scaling };
    }

    /// <summary>Reads the native outer rectangle rather than Avalonia's requested position.</summary>
    private static PixelRect Bounds(Window window)
    {
        var outer = JsonSerializer.SerializeToElement(WindowMetrics.Read(window)).GetProperty("Outer");
        return new PixelRect(outer.GetProperty("Left").GetInt32(), outer.GetProperty("Top").GetInt32(),
            outer.GetProperty("Right").GetInt32() - outer.GetProperty("Left").GetInt32(),
            outer.GetProperty("Bottom").GetInt32() - outer.GetProperty("Top").GetInt32());
    }

    /// <summary>Raises the fixture's own widget press through the same handler used by an actual mouse click.</summary>
    private static void Press(Window widget) => widget.RaiseEvent(new PointerPressedEventArgs(widget,
        new Pointer(1, PointerType.Mouse, true), widget, new Point(1, 1), 0,
        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
}
