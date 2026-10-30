using Reprise.Platform.Linux.Tray;
using Xunit;

namespace Reprise.Platform.Linux.Tests;

/// <summary>
/// Covers upgrades from older per-user shell extensions.
/// </summary>
public sealed class GnomeShellExtensionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"reprise-extension-{Guid.NewGuid():N}");

    /// <summary>
    /// A script fix replaces the installed copy even when metadata is unchanged.
    /// </summary>
    [Fact]
    public void SameVersionScriptFixReplacesOldCopy()
    {
        var source = Bundle("package", "pixel translation");
        var target = Bundle("user", "character slicing");

        Assert.True(GnomeShellExtension.SynchronizeBundle(source, target));
        Assert.Equal("pixel translation", File.ReadAllText(Path.Combine(target, "extension.js")));
        Assert.False(GnomeShellExtension.SynchronizeBundle(source, target));
    }

    /// <summary>
    /// An identical bundle keeps timestamps intact and does not require a reload.
    /// </summary>
    [Fact]
    public void UnchangedBundleIsNotRewritten()
    {
        var source = Bundle("package", "pixel translation");
        var target = Bundle("user", "pixel translation");
        var script = Path.Combine(target, "extension.js");
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(script, timestamp);

        Assert.False(GnomeShellExtension.SynchronizeBundle(source, target));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(script));
    }

    /// <summary>
    /// The archive can install into a user directory that does not yet exist.
    /// </summary>
    [Fact]
    public void MissingUserBundleIsInstalled()
    {
        var source = Bundle("package", "pixel translation");
        var target = Path.Combine(_root, "new-user");

        Assert.True(GnomeShellExtension.SynchronizeBundle(source, target));
        Assert.Equal(File.ReadAllText(Path.Combine(source, "metadata.json")),
            File.ReadAllText(Path.Combine(target, "metadata.json")));
        Assert.Equal("pixel translation", File.ReadAllText(Path.Combine(target, "extension.js")));
    }

    /// <summary>
    /// A broken package cannot partially overwrite a working installation.
    /// </summary>
    [Fact]
    public void IncompleteBundleLeavesInstalledFilesAlone()
    {
        var source = Bundle("package", "pixel translation");
        var target = Bundle("user", "working script");
        File.WriteAllText(Path.Combine(source, "metadata.json"), "new metadata");
        File.Delete(Path.Combine(source, "extension.js"));

        Assert.Throws<FileNotFoundException>(() => GnomeShellExtension.SynchronizeBundle(source, target));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "metadata.json")));
        Assert.Equal("working script", File.ReadAllText(Path.Combine(target, "extension.js")));
    }

    /// <summary>
    /// Debian installations use the current system copy without creating an override.
    /// </summary>
    [Fact]
    public void MatchingSystemBundleDoesNotCreateUserOverride()
    {
        var source = Bundle("package", "pixel translation");
        var system = Bundle("system", "pixel translation");
        var target = Path.Combine(_root, "new-user");

        Assert.False(GnomeShellExtension.SynchronizeBundle(source, target, system));
        Assert.False(Directory.Exists(target));
    }

    /// <summary>
    /// An old user copy is refreshed even when the system copy is already current.
    /// </summary>
    [Fact]
    public void ExistingUserOverrideIsRefreshedFromPackage()
    {
        var source = Bundle("package", "pixel translation");
        var system = Bundle("system", "pixel translation");
        var target = Bundle("user", "character slicing");

        Assert.True(GnomeShellExtension.SynchronizeBundle(source, target, system));
        Assert.Equal("pixel translation", File.ReadAllText(Path.Combine(target, "extension.js")));
    }

    /// <summary>
    /// A newer AppImage can override an older system package without root access.
    /// </summary>
    [Fact]
    public void NewerBundleOverridesOldSystemInstallation()
    {
        var source = Bundle("package", "pixel translation");
        var system = Bundle("system", "character slicing");
        var target = Path.Combine(_root, "new-user");

        Assert.True(GnomeShellExtension.SynchronizeBundle(source, target, system));
        Assert.Equal("pixel translation", File.ReadAllText(Path.Combine(target, "extension.js")));
    }

    /// <summary>
    /// Creates a complete extension fixture.
    /// </summary>
    /// <param name="name">Directory name below the test root.</param>
    /// <param name="script">Script content.</param>
    /// <returns>Path to the fixture.</returns>
    private string Bundle(string name, string script)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "metadata.json"), "{}");
        File.WriteAllText(Path.Combine(path, "extension.js"), script);
        return path;
    }

    /// <summary>
    /// Removes only this test's temporary files.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
