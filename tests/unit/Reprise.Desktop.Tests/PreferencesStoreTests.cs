using Reprise.Desktop;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>
/// Covers persistence of the panel settings.
/// </summary>
public sealed class PreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "reprise-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "nested", "preferences.json");

    /// <summary>
    /// A missing file yields the macOS defaults.
    /// </summary>
    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var store = new PreferencesStore(FilePath);

        Assert.Equal(new DesktopPreferences(), store.Current);
        Assert.Equal(PanelTheme.Liquid, store.Current.PanelTheme);
    }

    /// <summary>
    /// An update is written, then read back by a fresh store.
    /// </summary>
    [Fact]
    public void UpdateRoundTripsThroughTheFile()
    {
        var store = new PreferencesStore(FilePath);
        var notified = 0;
        store.Changed += (_, _) => notified++;

        store.Update(current => current with
        {
            PanelTheme = PanelTheme.Dark,
            TrailingTimeStyle = PanelTrailingTimeStyle.Duration,
            AutomaticallyScrollsTitles = false,
        });

        Assert.Equal(1, notified);
        Assert.True(File.Exists(FilePath));
        var reloaded = new PreferencesStore(FilePath);
        Assert.Equal(store.Current, reloaded.Current);
        Assert.Contains("\"panelTheme\": \"dark\"", File.ReadAllText(FilePath));
    }

    /// <summary>
    /// Re-applying the current value neither writes nor notifies.
    /// </summary>
    [Fact]
    public void UnchangedUpdateIsIgnored()
    {
        var store = new PreferencesStore(FilePath);
        var notified = 0;
        store.Changed += (_, _) => notified++;

        store.Update(current => current with { PanelTheme = current.PanelTheme });

        Assert.Equal(0, notified);
        Assert.False(File.Exists(FilePath));
    }

    /// <summary>
    /// A damaged file falls back to defaults instead of failing startup.
    /// </summary>
    [Fact]
    public void CorruptFileYieldsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");

        var store = new PreferencesStore(FilePath);

        Assert.Equal(new DesktopPreferences(), store.Current);
    }

    /// <summary>
    /// Removes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
