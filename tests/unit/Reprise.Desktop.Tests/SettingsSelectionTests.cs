using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>Exercises real dynamic resource bindings after repeated choices, palette changes, and reopening.</summary>
public sealed class SettingsSelectionTests
{
    /// <summary>Only the current option and tab may remain highlighted when the system theme refreshes.</summary>
    [Fact]
    public void PaletteRefreshCannotReactivatePreviouslySelectedOptions()
    {
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var picker = new SegmentedPicker(new[] { ("One", (object)1), ("Two", (object)2), ("Three", (object)3) }, 1);
        var tabs = new SettingsTabBar(new[] { ("One", PanelIcons.Gear), ("Two", PanelIcons.Palette), ("Three", PanelIcons.MenuBarRectangle) });
        var window = new Window { Content = new StackPanel { Children = { picker, tabs } } };
        window.Resources["SettingsCardBrush"] = Brushes.White;
        window.Resources["SettingsWellBrush"] = Brushes.Gray;
        window.Resources["SettingsAccentBrush"] = Brushes.Blue;
        window.Resources["SettingsSecondaryBrush"] = Brushes.Black;
        window.Show();
        for (var round = 0; round < 3; round++)
        {
            for (var index = 0; index < 3; index++)
            {
                picker.SelectedValue = index + 1;
                tabs.Select(index);
                window.Resources["SettingsCardBrush"] = new SolidColorBrush(Color.FromRgb((byte)(200 + index), 200, 200));
                window.Resources["SettingsWellBrush"] = new SolidColorBrush(Color.FromRgb(100, (byte)(100 + index), 100));
                Dispatcher.UIThread.RunJobs();
                var segments = ((Panel)picker.Child!).Children.Cast<Border>().ToArray();
                var tabCells = ((Panel)tabs.Child!).Children.Cast<Border>().ToArray();
                Assert.Single(segments, cell => cell.Background is ISolidColorBrush { Color.A: > 0 });
                Assert.Single(tabCells, cell => cell.Background is ISolidColorBrush { Color.A: > 0 });
                Assert.Equal(index + 1, picker.SelectedValue);
                Assert.Equal(index, tabs.SelectedIndex);
            }
            window.Hide();
            window.Show();
        }
        window.Close();
    }
}
