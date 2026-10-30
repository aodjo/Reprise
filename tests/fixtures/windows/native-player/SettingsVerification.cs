using System.Text.Json;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Reprise.Desktop;
using Reprise.Platform.Windows;
using Reprise.Platform.Windows.Media;

/// <summary>Exercises real Windows settings construction, tab transitions, preference changes, and reopening.</summary>
internal static class SettingsVerification
{
    /// <summary>Runs against the actual shared settings window with an isolated preference file.</summary>
    /// <param name="root">Task-owned diagnostic directory for preferences, renders, and results.</param>
    internal static async Task RunAsync(string root)
    {
        var stage = "construct";
        SettingsWindow? settings = null;
        try
        {
            var preferences = new PreferencesStore(Path.Combine(root, "settings-preferences.json"));
            using var model = new NowPlayingViewModel(new WindowsMediaSessionService(), preferences: () => preferences.Current);
            settings = new SettingsWindow(model, preferences, new WindowsApplicationUpdater(), new WindowsAutostartEntry());
            stage = "initial presentation";
            settings.Present();
            await Task.Delay(150);
            var tabs = settings.GetVisualDescendants().OfType<SettingsTabBar>().Single();
            if (!settings.GetVisualDescendants().OfType<TextBlock>().Any(item => item.Text == "멜론"))
                throw new InvalidOperationException("Melon is absent from the Windows display priority list.");
            var visited = new List<int>();
            for (var round = 0; round < 3; round++)
            {
                stage = $"open {round}";
                settings.Present();
                for (var index = 0; index < 6; index++)
                {
                    stage = $"round {round}, tab {index}";
                    tabs.Select(index);
                    await Task.Delay(150);
                    if (!settings.IsVisible || tabs.SelectedIndex != index)
                        throw new InvalidOperationException("The settings window or selected tab disappeared.");
                    if (index == 3)
                    {
                        var placement = settings.GetVisualDescendants().OfType<SegmentedPicker>()
                            .Single(item => item.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "왼쪽"));
                        PressSegment(placement, "왼쪽");
                        if (!preferences.Current.WindowsTaskbarOnLeft) throw new InvalidOperationException("Left placement did not persist.");
                        PressSegment(placement, "오른쪽");
                        if (preferences.Current.WindowsTaskbarOnLeft) throw new InvalidOperationException("Right placement did not persist.");
                    }
                    if (index == 5 && !settings.GetVisualDescendants().OfType<TextBlock>().Any(item => item.Text == "업데이트 자동 다운로드"))
                        throw new InvalidOperationException("Automatic update controls are absent.");
                    visited.Add(index);
                    if (round == 0)
                    {
                        using var render = new RenderTargetBitmap(new((int)settings.Width, (int)settings.Height));
                        render.Render(settings);
                        render.Save(Path.Combine(root, $"settings-tab-{index}.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                tabs.Select(0);
                stage = "change lyric setting";
                await Task.Delay(150);
                var label = settings.GetVisualDescendants().OfType<TextBlock>()
                    .Single(item => item.Text == "작업 표시줄에 가사 표시");
                var toggle = ((Grid)label.Parent!).GetVisualDescendants().OfType<ToggleSwitch>().Single();
                toggle.IsChecked = true;
                var reserveLabel = settings.GetVisualDescendants().OfType<TextBlock>()
                    .Single(item => item.Text == "가사 공간 확보");
                var reserve = ((Grid)reserveLabel.Parent!).GetVisualDescendants().OfType<ToggleSwitch>().Single();
                if (!reserve.IsEnabled) throw new InvalidOperationException("The Windows reservation control is disabled.");
                reserve.IsChecked = true;
                if (!preferences.Current.WindowsTaskbarReservesLabelWidth)
                    throw new InvalidOperationException("Enabling space reservation did not persist.");
                reserve.IsChecked = false;
                if (preferences.Current.WindowsTaskbarReservesLabelWidth ||
                    new PreferencesStore(Path.Combine(root, "settings-preferences.json")).Current.WindowsTaskbarReservesLabelWidth)
                    throw new InvalidOperationException("Disabling space reservation did not persist.");
                toggle.IsChecked = !preferences.Current.WindowsTaskbarShowsLyrics;
                if (toggle.IsChecked != preferences.Current.WindowsTaskbarShowsLyrics)
                    throw new InvalidOperationException("The lyric setting did not persist.");
                stage = "hide and reopen";
                settings.Close();
                if (settings.IsVisible) throw new InvalidOperationException("Closing settings did not hide it.");
            }
            File.WriteAllText(Path.Combine(root, "settings-result.json"), JsonSerializer.Serialize(new
            {
                ok = true, visited, rounds = 3, reservationPersisted = true, preferencePersisted = new PreferencesStore(
                    Path.Combine(root, "settings-preferences.json")).Current.WindowsTaskbarShowsLyrics == preferences.Current.WindowsTaskbarShowsLyrics,
            }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "settings-result.json"), JsonSerializer.Serialize(new { ok = false, stage, error = error.ToString() }));
            Environment.ExitCode = 1;
        }
        finally
        {
            if (settings is not null)
            {
                settings.AllowClose = true;
                settings.Close();
            }
        }
    }

    /// <summary>Raises a press on the fixture's own segment so the same handler as a user click persists the preference.</summary>
    private static void PressSegment(SegmentedPicker picker, string label)
    {
        var text = picker.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Text == label);
        var cell = (Border)text.Parent!;
        cell.RaiseEvent(new PointerPressedEventArgs(cell, new Pointer(1, PointerType.Mouse, true), cell, new Point(1, 1), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
    }
}
