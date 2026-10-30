namespace Reprise.Desktop;

/// <summary>Shares manual and scheduled update state while keeping automatic downloads separate from explicit installation.</summary>
public sealed class AutomaticApplicationUpdater : IApplicationUpdater, IDisposable
{
    /// <summary>Time between quiet feed checks after the initial startup delay.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private readonly IApplicationUpdater _inner;
    private readonly PreferencesStore _preferences;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cycle;
    private bool _checks;
    private bool _downloads;
    private bool _started;
    private bool _disposed;

    /// <summary>Connects a platform updater to persistent user preferences without starting network activity in the constructor.</summary>
    public AutomaticApplicationUpdater(IApplicationUpdater inner, PreferencesStore preferences)
    {
        _inner = inner;
        _preferences = preferences;
        _checks = preferences.Current.AutomaticallyChecksForUpdates;
        _downloads = preferences.Current.AutomaticallyDownloadsUpdates;
        preferences.Changed += PreferencesChanged;
    }

    /// <summary>Notifies views on the caller's thread when an update or its preparation state changes.</summary>
    public event EventHandler? Changed;
    /// <summary>Most recently checked eligible update, retained while the settings window is closed.</summary>
    public ApplicationUpdate? AvailableUpdate { get; private set; }
    /// <summary>Whether the currently available update has been downloaded and verified.</summary>
    public bool IsPrepared { get; private set; }
    /// <summary>Latest background progress or error, shown when settings is opened.</summary>
    public string Status { get; private set; } = string.Empty;
    /// <inheritdoc />
    public bool IsAvailable => _inner.IsAvailable;
    /// <inheritdoc />
    public bool SupportsPreparation => _inner.SupportsPreparation;
    /// <inheritdoc />
    public string UnavailableMessage => _inner.UnavailableMessage;

    /// <summary>Starts periodic checks once; background failures are reported as status rather than unhandled exceptions.</summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        RestartCycle();
    }

    /// <inheritdoc />
    public async Task<ApplicationUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var update = await _inner.CheckAsync(cancellationToken);
            if (update != AvailableUpdate) IsPrepared = false;
            AvailableUpdate = update;
            SetStatus(update is null ? "새로운 업데이트가 없습니다." : $"새 버전 {update.Version}을 설치할 수 있습니다.");
            if (update is not null && _preferences.Current.AutomaticallyChecksForUpdates &&
                _preferences.Current.AutomaticallyDownloadsUpdates && SupportsPreparation && !IsPrepared)
                await PrepareCoreAsync(update, new StatusProgress(this), cancellationToken);
            return update;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task PrepareAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await PrepareCoreAsync(update, progress, cancellationToken); }
        finally { _gate.Release(); }
    }

    /// <summary>Prepares a package while the operation gate is held; no installer is started here.</summary>
    private async Task PrepareCoreAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        await _inner.PrepareAsync(update, progress, cancellationToken);
        AvailableUpdate = update;
        IsPrepared = true;
        SetStatus("업데이트 다운로드를 마쳤습니다. 설치 버튼을 눌러 적용해 주세요.");
    }

    /// <inheritdoc />
    public async Task<ApplicationUpdateResult> InstallAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await _inner.InstallAsync(update, progress, cancellationToken); }
        finally { _gate.Release(); }
    }

    /// <summary>Restarts scheduling only for update preference changes, leaving unrelated theme and playback changes alone.</summary>
    private void PreferencesChanged(object? sender, EventArgs args)
    {
        var current = _preferences.Current;
        if (_checks == current.AutomaticallyChecksForUpdates && _downloads == current.AutomaticallyDownloadsUpdates) return;
        _checks = current.AutomaticallyChecksForUpdates;
        _downloads = current.AutomaticallyDownloadsUpdates;
        if (_started && !_disposed) RestartCycle();
    }

    /// <summary>Cancels only automatic work; explicit checks and installation remain available when automatic checks are disabled.</summary>
    private void RestartCycle()
    {
        _cycle?.Cancel();
        _cycle?.Dispose();
        _cycle = new CancellationTokenSource();
        if (_checks) _ = RunAsync(_cycle.Token);
    }

    /// <summary>Checks after startup and every six hours without interrupting playback or opening settings.</summary>
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), token);
            while (!token.IsCancellationRequested)
            {
                try { if (IsAvailable) await CheckAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception error) { SetStatus($"업데이트를 확인하지 못했습니다. {error.Message}"); }
                await Task.Delay(CheckInterval, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    /// <summary>Publishes one status update for both background and manual views.</summary>
    private void SetStatus(string message)
    {
        Status = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bridges download progress directly into retained updater state without requiring a UI synchronization context.</summary>
    private sealed class StatusProgress(AutomaticApplicationUpdater owner) : IProgress<UpdateProgress>
    {
        /// <inheritdoc />
        public void Report(UpdateProgress value) => owner.SetStatus(value.Message);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preferences.Changed -= PreferencesChanged;
        _cycle?.Cancel();
        _cycle?.Dispose();
    }
}
