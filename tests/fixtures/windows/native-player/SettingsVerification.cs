using System.Text.Json;
using Avalonia.Controls;
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
}
