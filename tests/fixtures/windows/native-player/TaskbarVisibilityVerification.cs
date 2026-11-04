using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Reprise.Desktop;
using Reprise.Platform.Windows.Taskbar;

/// <summary>Records real foreground and Reprise visibility without injecting input or changing another application's windows.</summary>
internal static class TaskbarVisibilityVerification
{
    /// <summary>Verifies exact fullscreen and one-pixel inset windows hide lyrics, then restore them after each child exits.</summary>
    internal static async Task VerifyFullscreenAsync(string root)
    {
        using var publisher = new WindowsTaskbarLyricsPublisher();
        var control = new Window { Title = "Reprise visibility regression", Width = 320, Height = 140,
            ShowInTaskbar = false, Topmost = true };
        Process? target = null;
        var stage = "show ordinary window";
        try
        {
            File.Delete(Path.Combine(root, "stop-fullscreen"));
            control.Show();
            control.Activate();
            await publisher.StartAsync();
            var state = new MenuBarState("Fullscreen regression", "Reprise 전체 화면 검사", [], true, true, false,
                30, 20, "Reprise 전체 화면 검사", true, false);
            Window? strip = null;
            TaskbarFrame? frame = null;
            for (var attempt = 0; attempt < 120; attempt++)
            {
                publisher.Publish(state);
                frame = WindowsTaskbarGeometry.Read();
                strip = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows
                    .FirstOrDefault(window => window.Title == "Reprise 작업 표시줄 가사" && window.IsVisible);
                if (frame is not null && strip is not null) break;
                await Task.Delay(250);
            }
            if (frame is null || strip is null) throw new InvalidOperationException("The diagnostic strip did not appear on the ordinary desktop.");
            var cases = new List<object>();
            foreach (var inset in new[] { false, true })
            {
                File.Delete(Path.Combine(root, "stop-fullscreen"));
                stage = inset ? "inset fullscreen child" : "fullscreen child";
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                start.ArgumentList.Add("--fullscreen-target");
                if (inset) start.ArgumentList.Add("--inset-fullscreen");
                target = Process.Start(start)!;
                var detected = false;
                for (var attempt = 0; attempt < 120; attempt++)
                {
                    publisher.Publish(state);
                    GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
                    detected = foregroundProcess == target.Id && WindowsTaskbarGeometry.IsFullscreen(frame.Monitor);
                    if (detected && !strip.IsVisible) break;
                    await Task.Delay(250);
                }
                if (!detected || strip.IsVisible)
                {
                    GetWindowThreadProcessId(GetForegroundWindow(), out var activeProcess);
                    GetWindowRect(GetForegroundWindow(), out var activeBounds);
                    var activeClass = new StringBuilder(256);
                    GetClassName(GetForegroundWindow(), activeClass, activeClass.Capacity);
                    throw new InvalidOperationException($"Fullscreen verification failed: foreground={activeProcess}, target={target.Id}, " +
                        $"class={activeClass}, bounds={activeBounds.Left},{activeBounds.Top},{activeBounds.Right},{activeBounds.Bottom}, " +
                        $"monitor={frame.Monitor}, detected={detected}, stripVisible={strip.IsVisible}.");
                }
                stage = "restore desktop";
                File.WriteAllText(Path.Combine(root, "stop-fullscreen"), "stop");
                await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                control.Activate();
                for (var attempt = 0; attempt < 40 && !strip.IsVisible; attempt++)
                {
                    publisher.Publish(state);
                    await Task.Delay(250);
                }
                if (!strip.IsVisible) throw new InvalidOperationException("The strip did not return after fullscreen ended.");
                cases.Add(new { inset, fullscreenDetected = detected, hiddenDuringFullscreen = true, restored = strip.IsVisible });
                target.Dispose();
                target = null;
            }
            File.WriteAllText(Path.Combine(root, "fullscreen-result.json"), JsonSerializer.Serialize(new
            { ok = true, cases }));
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(root, "fullscreen-result.json"), JsonSerializer.Serialize(new { ok = false, stage, error = error.ToString() }));
        }
        finally
        {
            if (target is not null)
            {
                if (!target.HasExited) target.Kill();
                target.Dispose();
            }
            control.Close();
        }
    }

    /// <summary>Runs only the fixture-owned fullscreen target; no other application's windows or keyboard input are modified.</summary>
    internal static async Task RunFullscreenTargetAsync(string root, bool inset)
    {
        var window = new Window { Title = "Reprise fullscreen regression target", WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false, Topmost = true, WindowState = inset ? WindowState.Normal : WindowState.FullScreen,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Avalonia.Media.Brushes.MidnightBlue,
            Content = new TextBlock { Text = "Reprise 전체 화면 표시 검사", FontSize = 32,
                Foreground = Avalonia.Media.Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } };
        try
        {
            window.Show();
            if (inset)
            {
                var monitor = window.Screens.Primary!.Bounds;
                window.Position = new PixelPoint(monitor.X + 1, monitor.Y + 1);
                window.Width = (monitor.Width - 2) / window.RenderScaling;
                window.Height = (monitor.Height - 2) / window.RenderScaling;
            }
            window.Activate();
            while (window.IsVisible && !File.Exists(Path.Combine(root, "stop-fullscreen"))) await Task.Delay(100);
        }
        finally { window.Close(); }
    }

    /// <summary>Observes actual shell visibility, optionally publishing an isolated diagnostic strip while the user exercises Start.</summary>
    internal static async Task ObserveAsync(string root, bool watch = false)
    {
        using var publisher = watch ? new WindowsTaskbarLyricsPublisher() : null;
        if (publisher is not null) await publisher.StartAsync();
        var state = new MenuBarState("Reprise visibility diagnostic", "Reprise 시작 메뉴 표시 검사", [],
            true, true, false, 30, 20, "Reprise 시작 메뉴 표시 검사", true, false);
        var observations = new List<object>();
        for (var index = 0; index < (watch ? 480 : 10) && !File.Exists(Path.Combine(root, "stop-visibility")); index++)
        {
            publisher?.Publish(state);
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var processId);
            var name = new StringBuilder(256);
            GetClassName(foreground, name, name.Capacity);
            GetWindowRect(foreground, out var bounds);
            string? path = null;
            try { using var process = Process.GetProcessById((int)processId); path = process.MainModule?.FileName; }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException) { }
            var frame = WindowsTaskbarGeometry.Read();
            var strip = FindWindow(null, "Reprise 작업 표시줄 가사");
            if (watch)
                strip = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows
                    .FirstOrDefault(window => window.Title == "Reprise 작업 표시줄 가사")?.TryGetPlatformHandle()?.Handle ?? 0;
            var bar = FindWindow("Shell_TrayWnd", null);
            GetWindowRect(bar, out var barBounds);
            observations.Add(new
            {
                time = DateTimeOffset.UtcNow, foreground = foreground.ToInt64(), processId, path,
                windowClass = name.ToString(), bounds = new { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom },
                frame, classifiedFullscreen = frame is not null && WindowsTaskbarGeometry.IsFullscreen(frame.Monitor),
                strip = strip.ToInt64(), stripVisible = strip != 0 && IsWindowVisible(strip),
                bar = bar.ToInt64(), barVisible = bar != 0 && IsWindowVisible(bar),
                barBounds = new { barBounds.Left, barBounds.Top, barBounds.Right, barBounds.Bottom },
                children = WindowsTaskbarGeometry.Diagnose(),
                queryStatus = TaskbarAccessibility.QueryStatus,
                accessibilityError = TaskbarAccessibility.LastError,
            });
            // A guest-agent read can temporarily hold the file; retain observations in memory and retry on the next sample.
            try { File.WriteAllText(Path.Combine(root, "visibility-observation.json"), JsonSerializer.Serialize(observations)); }
            catch (IOException) { }
            await Task.Delay(500);
        }
    }

    /// <summary>Screen rectangle returned by Win32.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    /// <summary>Reads foreground metadata without changing focus.</summary>
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    /// <summary>Identifies the foreground process.</summary>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    /// <summary>Reads the foreground's screen extent.</summary>
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    /// <summary>Reads the native foreground class.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    /// <summary>Finds the already-installed Reprise lyric window without activating it.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern nint FindWindow(string? windowClass, string? title);
    /// <summary>Checks the lyric window's actual native visibility.</summary>
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
}
