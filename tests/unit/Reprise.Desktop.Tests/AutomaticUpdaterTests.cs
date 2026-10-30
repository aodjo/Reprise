using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>Verifies persistent automatic-update preferences without allowing background checks to install anything.</summary>
public sealed class AutomaticUpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "reprise-auto-update-" + Guid.NewGuid().ToString("N"));

    /// <summary>Automatic download prepares at most once per candidate, while installation always requires an explicit call.</summary>
    [Fact]
    public async Task AutomaticChecksAndDownloadsNeverInstall()
    {
        var preferences = new PreferencesStore(Path.Combine(_root, "preferences.json"));
        var backend = new Backend();
        using var updater = new AutomaticApplicationUpdater(backend, preferences);
        await updater.CheckAsync();
        Assert.Equal(0, backend.Prepared);
        preferences.Update(p => p with { AutomaticallyDownloadsUpdates = true });
        await updater.CheckAsync();
        await updater.CheckAsync();
        Assert.Equal(1, backend.Prepared);
        Assert.True(updater.IsPrepared);
        Assert.Equal(0, backend.Installed);
        Assert.True(new PreferencesStore(Path.Combine(_root, "preferences.json")).Current.AutomaticallyDownloadsUpdates);
        preferences.Update(p => p with { AutomaticallyChecksForUpdates = false });
        backend.Update = backend.Update with { Version = "2.0.2" };
        Assert.Equal("2.0.2", (await updater.CheckAsync())!.Version);
        Assert.Equal(1, backend.Prepared);
        Assert.False(updater.IsPrepared);
        await updater.InstallAsync(updater.AvailableUpdate!, new Progress<UpdateProgress>());
        Assert.Equal(1, backend.Installed);
    }

    /// <summary>Failed verification is never reported as a prepared update or followed by an installer invocation.</summary>
    [Fact]
    public async Task PreparationFailureCannotBecomeAReadyUpdate()
    {
        var preferences = new PreferencesStore(Path.Combine(_root, "preferences.json"));
        preferences.Update(p => p with { AutomaticallyDownloadsUpdates = true });
        var backend = new Backend { RejectPreparation = true };
        using var updater = new AutomaticApplicationUpdater(backend, preferences);
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.CheckAsync());
        Assert.False(updater.IsPrepared);
        Assert.Equal(0, backend.Installed);
    }

    /// <summary>Controlled platform boundary for sequencing, avoiding network access and real installers.</summary>
    private sealed class Backend : IApplicationUpdater
    {
        public bool IsAvailable => true;
        public bool SupportsPreparation => true;
        public int Prepared { get; private set; }
        public int Installed { get; private set; }
        public bool RejectPreparation { get; init; }
        public ApplicationUpdate Update { get; set; } = new("2.0.0", "2.0.1", new("https://updates.example/package"), new('a', 64), 3);
        /// <inheritdoc />
        public Task<ApplicationUpdate?> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult<ApplicationUpdate?>(Update);
        /// <inheritdoc />
        public Task PrepareAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress, CancellationToken cancellationToken = default)
        {
            Prepared++;
            return RejectPreparation ? Task.FromException(new InvalidDataException("digest mismatch")) : Task.CompletedTask;
        }
        /// <inheritdoc />
        public Task<ApplicationUpdateResult> InstallAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress, CancellationToken cancellationToken = default)
        {
            Installed++;
            return Task.FromResult(new ApplicationUpdateResult("Installed"));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
