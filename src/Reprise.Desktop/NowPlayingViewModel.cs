using System.ComponentModel;
using System.Runtime.CompilerServices;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Which player mark the panel shows beside the track.
/// </summary>
public enum PanelPlayerLogo
{
    /// <summary>
    /// Any MPRIS player without a dedicated mark; drawn as a music note.
    /// </summary>
    Generic,

    /// <summary>
    /// The Spotify desktop client.
    /// </summary>
    Spotify,

    /// <summary>
    /// YouTube Music, whether through a browser tab or a wrapper app.
    /// </summary>
    YouTubeMusic,
}

/// <summary>
/// Progress of the lyrics lookup for the current track.
/// </summary>
public enum LyricsLoadState
{
    /// <summary>
    /// No track, so nothing to look up.
    /// </summary>
    Idle,

    /// <summary>
    /// A lookup is in flight.
    /// </summary>
    Loading,

    /// <summary>
    /// Synced lyrics were found and are in <see cref="NowPlayingViewModel.Lyrics"/>.
    /// </summary>
    Available,

    /// <summary>
    /// No service had synced lyrics for the track.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Presentation state for the player panel.
/// </summary>
/// <remarks>
/// Sits between the platform's <see cref="IMediaSessionService"/> and the
/// window, turning a polled list of sessions into the single session, the
/// artwork, the scrubber position, and the volume level the panel binds to.
/// It also owns the two interactions that outlive a single poll - a seek in
/// progress and a volume change in flight - so the window never has to
/// reconcile a stale sample against what the user just did.
/// <para>
/// Not thread-safe by design: it is driven from the UI thread, and the only
/// concurrency it guards against is its own asynchronous completions.
/// </para>
/// </remarks>
public sealed class NowPlayingViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// Polls a pending seek may go unconfirmed before it is abandoned.
    /// </summary>
    /// <remarks>
    /// A player that ignores the seek would otherwise pin the scrubber at
    /// the requested position forever.
    /// </remarks>
    private const int MaximumUnconfirmedSeekPolls = 3;

    private readonly IMediaSessionService _mediaSessionService;
    private readonly IArtworkLoader _artworkLoader;
    private readonly ILyricsService? _lyricsService;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<LyricsTrackQuery, SyncedLyrics?> _lyricsCache = [];
    private LyricsTrackQuery? _lyricsQuery;
    private CancellationTokenSource? _lyricsLoad;
    private SyncedLyrics? _lyrics;
    private LyricsLoadState _lyricsState;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, int> _lastAudibleVolumes = new(StringComparer.Ordinal);
    private MediaSessionSnapshot? _activeSession;
    private byte[]? _artworkData;
    private Uri? _artworkUri;
    private CancellationTokenSource? _artworkLoad;
    private string? _pollError;
    private string? _commandError;
    private int _volume = PlayerVolume.Maximum;
    private bool _isVolumeEditing;
    private bool _volumeSendInFlight;
    private int? _queuedVolume;
    private TimeSpan _seekPosition;
    private bool _isSeeking;
    private TimeSpan? _pendingSeek;
    private int _unconfirmedSeekPolls;
    private string? _trackIdentity;
    private bool _disposed;

    /// <summary>
    /// Creates a view model over the given media session backend.
    /// </summary>
    /// <param name="mediaSessionService">
    /// Platform backend polled for sessions and used to dispatch commands.
    /// </param>
    /// <param name="artworkLoader">
    /// Fetches cover art. Defaults to <see cref="ArtworkLoader"/>.
    /// </param>
    /// <param name="timeProvider">
    /// Clock used to project playback position. Defaults to the system clock.
    /// </param>
    /// <param name="lyricsService">
    /// Looks up synced lyrics. Null disables lyrics entirely.
    /// </param>
    /// <example>
    /// <code>
    /// var viewModel = new NowPlayingViewModel(
    ///     new MprisMediaSessionService(),
    ///     lyricsService: new LyricsService());
    /// </code>
    /// </example>
    public NowPlayingViewModel(
        IMediaSessionService mediaSessionService,
        IArtworkLoader? artworkLoader = null,
        TimeProvider? timeProvider = null,
        ILyricsService? lyricsService = null)
    {
        _mediaSessionService = mediaSessionService;
        _artworkLoader = artworkLoader ?? new ArtworkLoader();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lyricsService = lyricsService;
    }

    /// <summary>
    /// Raised on the calling thread whenever a bound property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The session currently on display, or null when nothing is playing.
    /// </summary>
    public MediaSessionSnapshot? ActiveSession
    {
        get => _activeSession;
        private set
        {
            if (Equals(_activeSession, value))
            {
                return;
            }

            _activeSession = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Encoded cover art for <see cref="ActiveSession"/>, or null while it
    /// loads or when the player publishes none.
    /// </summary>
    public byte[]? ArtworkData
    {
        get => _artworkData;
        private set
        {
            if (ReferenceEquals(_artworkData, value))
            {
                return;
            }

            _artworkData = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Synced lyrics for the current track, or null when none are loaded.
    /// </summary>
    public SyncedLyrics? Lyrics => _lyrics;

    /// <summary>
    /// Where the lyrics lookup for the current track stands.
    /// </summary>
    public LyricsLoadState LyricsState => _lyricsState;

    /// <summary>
    /// The lyric line to show at a moment.
    /// </summary>
    /// <remarks>
    /// Uses the focused line rather than the strictly current one, so the
    /// last sung line stays up through an instrumental gap instead of the
    /// label flickering empty.
    /// </remarks>
    /// <param name="now">Moment to evaluate at.</param>
    /// <returns>The line, or null before the first line or without lyrics.</returns>
    /// <example>
    /// <code>
    /// var line = viewModel.CurrentLyricLine(DateTimeOffset.UtcNow);
    /// </code>
    /// </example>
    public LyricLine? CurrentLyricLine(DateTimeOffset now)
    {
        if (_lyrics is not { } lyrics)
        {
            return null;
        }

        return lyrics.FocusedLineIndex(DisplayedPosition(now)) is { } index
            ? lyrics.Lines[index]
            : null;
    }

    /// <summary>
    /// The lyric line to show right now.
    /// </summary>
    /// <returns>The line, or null.</returns>
    public LyricLine? CurrentLyricLine() => CurrentLyricLine(_timeProvider.GetUtcNow());

    /// <summary>
    /// Message from the most recent failure, or null while things are fine.
    /// </summary>
    /// <remarks>
    /// A failed command outranks a failed poll, since it is the more recent
    /// thing the user did.
    /// </remarks>
    public string? ErrorText => _commandError ?? _pollError;

    /// <summary>
    /// Whether there is a player to control at all.
    /// </summary>
    public bool IsRunning => _activeSession is not null;

    /// <summary>
    /// Whether the active session is currently playing.
    /// </summary>
    public bool IsPlaying => _activeSession?.Status == PlaybackStatus.Playing;

    /// <summary>
    /// Whether the active player reports a volume the panel can adjust.
    /// </summary>
    public bool HasVolume => _activeSession?.Volume is not null;

    /// <summary>
    /// Volume in percent, as the slider should show it.
    /// </summary>
    /// <remarks>
    /// Follows the player's reported level except while the user is editing
    /// or a change is still in flight, when it holds the user's value so the
    /// slider does not snap back to a stale sample mid-drag.
    /// </remarks>
    public int Volume
    {
        get => _volume;
        private set
        {
            if (_volume == value)
            {
                return;
            }

            _volume = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the user is dragging the volume slider right now.
    /// </summary>
    /// <remarks>
    /// Set by the window around a drag so polls do not overwrite the slider.
    /// </remarks>
    public bool IsVolumeEditing
    {
        get => _isVolumeEditing;
        set => _isVolumeEditing = value;
    }

    /// <summary>
    /// Whether the user is dragging the scrubber right now.
    /// </summary>
    public bool IsSeeking => _isSeeking;

    /// <summary>
    /// Length of the current track, or zero when the player reports none.
    /// </summary>
    public TimeSpan Duration => _activeSession?.Duration is { Ticks: > 0 } duration
        ? duration
        : TimeSpan.Zero;

    /// <summary>
    /// Which player mark to draw for the active session.
    /// </summary>
    public PanelPlayerLogo PlayerLogo => LogoFor(_activeSession?.PlayerId);

    /// <summary>
    /// Position the scrubber should show at a given moment.
    /// </summary>
    /// <remarks>
    /// Resolved in priority order: the point the user is dragging to, then a
    /// seek that was sent but not yet reflected by the player, then the last
    /// sample projected forward by the time since it was taken.
    /// </remarks>
    /// <param name="now">Moment to project the last sample to.</param>
    /// <returns>A position clamped to the current track.</returns>
    /// <example>
    /// <code>
    /// var shown = viewModel.DisplayedPosition(DateTimeOffset.UtcNow);
    /// </code>
    /// </example>
    public TimeSpan DisplayedPosition(DateTimeOffset now)
    {
        var session = _activeSession;
        if (session is null)
        {
            return TimeSpan.Zero;
        }

        if (_isSeeking)
        {
            return PlaybackPosition.Clamp(_seekPosition, session.Duration);
        }

        if (_pendingSeek is { } pending)
        {
            return PlaybackPosition.Clamp(pending, session.Duration);
        }

        return PlaybackPosition.Estimate(session, now);
    }

    /// <summary>
    /// Position the scrubber should show right now.
    /// </summary>
    /// <returns>A position clamped to the current track.</returns>
    public TimeSpan DisplayedPosition() => DisplayedPosition(_timeProvider.GetUtcNow());

    /// <summary>
    /// Polls the backend once and republishes the resulting state.
    /// </summary>
    /// <remarks>
    /// Called every second by the window's timer. A refresh already in flight
    /// causes this call to return immediately rather than queue: with a fixed
    /// tick, queueing a backend slower than the interval would build a
    /// backlog of polls whose results are stale by the time they arrive.
    /// <para>
    /// Failures are surfaced through <see cref="ErrorText"/> instead of
    /// thrown, because the caller is a timer tick with nowhere to report to,
    /// and a transient bus error should not tear down the UI. Cancellation
    /// during disposal is swallowed for the same reason - it is expected
    /// shutdown, not a fault.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A task that completes once the poll has finished and properties have
    /// been updated. Never faults.
    /// </returns>
    /// <example>
    /// <code>
    /// timer.Tick += async (_, _) => await viewModel.RefreshAsync();
    /// </code>
    /// </example>
    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (!await _refreshGate.WaitAsync(0, _lifetime.Token))
        {
            return;
        }

        try
        {
            var sessions = await _mediaSessionService.GetSessionsAsync(_lifetime.Token);
            Apply(ActiveSessionSelector.Select(sessions));
            SetPollError(null);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Apply(null);
            SetPollError(exception.Message);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Sends a playback command to the session currently on display.
    /// </summary>
    /// <remarks>
    /// Targets the snapshot the user is looking at rather than re-reading the
    /// active session, so a poll landing between the click and the dispatch
    /// cannot redirect the command to a different player. Refreshes
    /// immediately afterwards so the transport buttons reflect the new state
    /// without waiting out the remainder of the timer interval.
    /// </remarks>
    /// <param name="command">Transport control to invoke.</param>
    /// <returns>
    /// A task that completes once the command has been sent and the state
    /// re-read. Does nothing when no session is active. Never faults;
    /// failures land in <see cref="ErrorText"/>.
    /// </returns>
    /// <example>
    /// <code>
    /// nextButton.Click += async (_, _) =>
    ///     await viewModel.SendAsync(PlaybackCommand.Next);
    /// </code>
    /// </example>
    public async Task SendAsync(PlaybackCommand command)
    {
        if (_disposed || _activeSession is not { } session)
        {
            return;
        }

        await Guarded(async () =>
        {
            await _mediaSessionService.SendCommandAsync(
                session.PlayerId,
                command,
                _lifetime.Token);
            await RefreshAsync();
        });
    }

    /// <summary>
    /// Starts a scrub at the given position.
    /// </summary>
    /// <remarks>
    /// From here until <see cref="EndSeekAsync"/>, the scrubber shows the
    /// dragged value rather than the player's, and any earlier pending seek
    /// is forgotten because the user has overridden it.
    /// </remarks>
    /// <param name="position">Where the drag started.</param>
    public void BeginSeek(TimeSpan position)
    {
        _pendingSeek = null;
        _unconfirmedSeekPolls = 0;
        _seekPosition = position;
        _isSeeking = true;
        OnPropertyChanged(nameof(IsSeeking));
    }

    /// <summary>
    /// Moves an in-progress scrub.
    /// </summary>
    /// <param name="position">Where the pointer is now.</param>
    public void UpdateSeek(TimeSpan position)
    {
        if (!_isSeeking)
        {
            return;
        }

        _seekPosition = position;
    }

    /// <summary>
    /// Finishes a scrub by sending the final position to the player.
    /// </summary>
    /// <remarks>
    /// The target stays on display as a pending seek until a poll confirms
    /// it, so the scrubber does not jump back to the old position for the
    /// second it takes the player to catch up. A seek the player rejects is
    /// dropped at once, since nothing will ever confirm it.
    /// </remarks>
    /// <returns>
    /// A task that completes once the seek has been sent and the state
    /// re-read. Never faults; failures land in <see cref="ErrorText"/>.
    /// </returns>
    public async Task EndSeekAsync()
    {
        if (!_isSeeking)
        {
            return;
        }

        _isSeeking = false;
        OnPropertyChanged(nameof(IsSeeking));
        await SeekAsync(_seekPosition);
    }

    /// <summary>
    /// Moves playback of the active session to a position.
    /// </summary>
    /// <param name="position">Target offset from the start of the track.</param>
    /// <returns>
    /// A task that completes once the seek has been sent and the state
    /// re-read. Does nothing when no session is active. Never faults;
    /// failures land in <see cref="ErrorText"/>.
    /// </returns>
    /// <example>
    /// <code>
    /// await viewModel.SeekAsync(TimeSpan.FromSeconds(90));
    /// </code>
    /// </example>
    public async Task SeekAsync(TimeSpan position)
    {
        if (_disposed || _activeSession is not { } session)
        {
            return;
        }

        var target = PlaybackPosition.Clamp(position, session.Duration);
        _pendingSeek = target;
        _unconfirmedSeekPolls = 0;

        var accepted = await Guarded(() =>
            _mediaSessionService.SeekAsync(session.PlayerId, target, _lifetime.Token));
        if (!accepted && _pendingSeek == target)
        {
            _pendingSeek = null;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Sets the active session's volume.
    /// </summary>
    /// <remarks>
    /// Slider drags arrive faster than a bus round trip, so sends are
    /// coalesced: while one is in flight the newest level waits, and only
    /// the latest waiting value is sent afterwards. The slider itself is
    /// updated immediately so it tracks the pointer.
    /// </remarks>
    /// <param name="percent">Level from 0 to 100.</param>
    /// <returns>
    /// A task that completes once this level, or a newer one that superseded
    /// it, has been sent. Does nothing when no session is active. Never
    /// faults; failures land in <see cref="ErrorText"/>.
    /// </returns>
    /// <example>
    /// <code>
    /// await viewModel.SetVolumeAsync(35);
    /// </code>
    /// </example>
    public async Task SetVolumeAsync(int percent)
    {
        if (_disposed || _activeSession is not { } session)
        {
            return;
        }

        var level = PlayerVolume.Clamp(percent);
        Volume = level;
        RememberAudibleVolume(session.PlayerId, level);

        if (_volumeSendInFlight)
        {
            _queuedVolume = level;
            return;
        }

        _volumeSendInFlight = true;
        try
        {
            int? next = level;
            while (next is { } value)
            {
                _queuedVolume = null;
                await Guarded(() => _mediaSessionService.SetVolumeAsync(
                    session.PlayerId,
                    PlayerVolume.ToFraction(value),
                    _lifetime.Token));
                next = _queuedVolume;
            }
        }
        finally
        {
            _volumeSendInFlight = false;
        }
    }

    /// <summary>
    /// Mutes the active session, or restores its last audible level.
    /// </summary>
    /// <returns>
    /// A task that completes once the new level has been sent. Does nothing
    /// when no session is active.
    /// </returns>
    /// <example>
    /// <code>
    /// muteButton.Click += async (_, _) => await viewModel.ToggleMuteAsync();
    /// </code>
    /// </example>
    public Task ToggleMuteAsync()
    {
        if (_activeSession is not { } session)
        {
            return Task.CompletedTask;
        }

        _lastAudibleVolumes.TryGetValue(session.PlayerId, out var lastAudible);
        var target = PlayerVolume.MuteToggleTarget(
            Volume,
            lastAudible > 0 ? lastAudible : null);
        return SetVolumeAsync(target);
    }

    /// <summary>
    /// Cancels any in-flight work and blocks further backend calls.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once. The refresh gate is intentionally not
    /// disposed: a poll may still be unwinding through its finally block, and
    /// releasing a disposed semaphore would throw on a path that has no
    /// handler. The disposed flag is what actually stops new work.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _artworkLoad?.Cancel();
        _artworkLoad?.Dispose();
        _artworkLoad = null;
        _lyricsLoad?.Cancel();
        _lyricsLoad?.Dispose();
        _lyricsLoad = null;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>
    /// Maps a player id onto the mark the panel draws for it.
    /// </summary>
    /// <remarks>
    /// Matches on the bus-name stem, which browsers extend with an instance
    /// suffix and wrapper apps prefix with their own name.
    /// </remarks>
    /// <param name="playerId">Player id, or null for no session.</param>
    /// <returns>The mark to draw.</returns>
    /// <example>
    /// <code>
    /// NowPlayingViewModel.LogoFor("spotify"); // PanelPlayerLogo.Spotify
    /// </code>
    /// </example>
    public static PanelPlayerLogo LogoFor(string? playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            return PanelPlayerLogo.Generic;
        }

        if (playerId.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return PanelPlayerLogo.Spotify;
        }

        return playerId.Contains("youtube", StringComparison.OrdinalIgnoreCase)
            || playerId.Contains("YoutubeMusic", StringComparison.OrdinalIgnoreCase)
            ? PanelPlayerLogo.YouTubeMusic
            : PanelPlayerLogo.Generic;
    }

    /// <summary>
    /// Publishes a freshly selected session and reconciles the derived state.
    /// </summary>
    /// <remarks>
    /// Handles the three things a new sample can invalidate: a pending seek
    /// that the sample now confirms or that has waited too long, a volume
    /// level the user is not currently holding, and artwork whose URI has
    /// changed. A change of track or player clears any seek outright, since
    /// a position in the old track means nothing in the new one.
    /// </remarks>
    /// <param name="session">The session to display, or null.</param>
    private void Apply(MediaSessionSnapshot? session)
    {
        var identity = session is null
            ? null
            : string.Join('\0', session.PlayerId, session.Title, session.Album, session.Artist);
        if (identity != _trackIdentity)
        {
            _trackIdentity = identity;
            _isSeeking = false;
            _pendingSeek = null;
            _unconfirmedSeekPolls = 0;
        }

        ActiveSession = session;
        ReconcilePendingSeek(session);
        ReconcileVolume(session);
        ReconcileArtwork(session?.ArtworkUri);
        ReconcileLyrics(session);
    }

    /// <summary>
    /// Starts a lyrics lookup when the track changes, serving repeats from cache.
    /// </summary>
    /// <remarks>
    /// Keyed by the loose <see cref="LyricsTrackQuery"/> so a player that
    /// re-reports the same song with cosmetic differences does not trigger
    /// another network round trip. Misses are cached too, since asking
    /// again a second later will not make lyrics appear.
    /// </remarks>
    /// <param name="session">Latest sample, or null.</param>
    private void ReconcileLyrics(MediaSessionSnapshot? session)
    {
        if (_lyricsService is null)
        {
            return;
        }

        var query = session is null ? null : LyricsTrackQuery.From(session);
        if (Equals(query, _lyricsQuery) && (query is null || _lyricsState != LyricsLoadState.Idle))
        {
            return;
        }

        _lyricsLoad?.Cancel();
        _lyricsLoad?.Dispose();
        _lyricsLoad = null;
        _lyricsQuery = query;

        if (query is null)
        {
            SetLyrics(null, LyricsLoadState.Idle);
            return;
        }

        if (_lyricsCache.TryGetValue(query, out var cached))
        {
            SetLyrics(cached, cached is null ? LyricsLoadState.Unavailable : LyricsLoadState.Available);
            return;
        }

        SetLyrics(null, LyricsLoadState.Loading);
        var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _lyricsLoad = load;
        _ = LoadLyricsAsync(query, load.Token);
    }

    /// <summary>
    /// Fetches lyrics and publishes them if the track is still current.
    /// </summary>
    /// <param name="query">Track to look up.</param>
    /// <param name="cancellationToken">Cancelled when the track changes.</param>
    /// <returns>A task that completes when the result has been applied or dropped.</returns>
    private async Task LoadLyricsAsync(LyricsTrackQuery query, CancellationToken cancellationToken)
    {
        SyncedLyrics? lyrics;
        try
        {
            lyrics = await _lyricsService!.FetchSyncedLyricsAsync(query, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            lyrics = null;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _lyricsCache[query] = lyrics;
        if (Equals(query, _lyricsQuery))
        {
            SetLyrics(lyrics, lyrics is null ? LyricsLoadState.Unavailable : LyricsLoadState.Available);
        }
    }

    /// <summary>
    /// Publishes a lyrics result and its state together.
    /// </summary>
    /// <param name="lyrics">Lyrics to show, or null.</param>
    /// <param name="state">Lookup state to report.</param>
    private void SetLyrics(SyncedLyrics? lyrics, LyricsLoadState state)
    {
        var changed = !ReferenceEquals(_lyrics, lyrics) || _lyricsState != state;
        _lyrics = lyrics;
        _lyricsState = state;
        if (changed)
        {
            OnPropertyChanged(nameof(Lyrics));
            OnPropertyChanged(nameof(LyricsState));
        }
    }

    /// <summary>
    /// Drops a pending seek once the player reflects it or stops waiting.
    /// </summary>
    /// <param name="session">Latest sample, or null.</param>
    private void ReconcilePendingSeek(MediaSessionSnapshot? session)
    {
        if (_pendingSeek is not { } pending || _isSeeking)
        {
            return;
        }

        var confirmed = session?.Position is { } actual
            && PlaybackPosition.ConfirmsSeek(actual, pending);
        if (confirmed || ++_unconfirmedSeekPolls >= MaximumUnconfirmedSeekPolls)
        {
            _pendingSeek = null;
            _unconfirmedSeekPolls = 0;
        }
    }

    /// <summary>
    /// Follows the player's volume unless the user is holding the slider.
    /// </summary>
    /// <param name="session">Latest sample, or null.</param>
    private void ReconcileVolume(MediaSessionSnapshot? session)
    {
        if (session?.Volume is not { } fraction || _isVolumeEditing || _volumeSendInFlight)
        {
            return;
        }

        var level = PlayerVolume.FromFraction(fraction);
        Volume = level;
        RememberAudibleVolume(session.PlayerId, level);
    }

    /// <summary>
    /// Starts loading artwork when its URI changes, cancelling any prior load.
    /// </summary>
    /// <param name="uri">Artwork location of the current session, or null.</param>
    private void ReconcileArtwork(Uri? uri)
    {
        if (uri == _artworkUri)
        {
            return;
        }

        _artworkLoad?.Cancel();
        _artworkLoad?.Dispose();
        _artworkLoad = null;
        _artworkUri = uri;

        if (uri is null)
        {
            ArtworkData = null;
            return;
        }

        var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _artworkLoad = load;
        _ = LoadArtworkAsync(uri, load.Token);
    }

    /// <summary>
    /// Fetches artwork and publishes it if it is still the wanted one.
    /// </summary>
    /// <param name="uri">Location to load.</param>
    /// <param name="cancellationToken">Cancelled when a newer URI supersedes this one.</param>
    /// <returns>A task that completes when the artwork has been applied or dropped.</returns>
    private async Task LoadArtworkAsync(Uri uri, CancellationToken cancellationToken)
    {
        byte[]? bytes;
        try
        {
            bytes = await _artworkLoader.LoadAsync(uri, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested && uri == _artworkUri)
        {
            ArtworkData = bytes;
        }
    }

    /// <summary>
    /// Records a non-zero level so a later unmute can restore it.
    /// </summary>
    /// <param name="playerId">Player the level belongs to.</param>
    /// <param name="level">Level in percent.</param>
    private void RememberAudibleVolume(string playerId, int level)
    {
        if (level > PlayerVolume.Minimum)
        {
            _lastAudibleVolumes[playerId] = level;
        }
    }

    /// <summary>
    /// Runs a command, routing its failure into <see cref="ErrorText"/>.
    /// </summary>
    /// <param name="action">Backend call to run.</param>
    /// <returns>
    /// A task that never faults, yielding true when the command succeeded.
    /// </returns>
    private async Task<bool> Guarded(Func<Task> action)
    {
        try
        {
            await action();
            SetCommandError(null);
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetCommandError(exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Updates the poll error and notifies if the visible message changed.
    /// </summary>
    /// <param name="message">New poll error, or null.</param>
    private void SetPollError(string? message)
    {
        var before = ErrorText;
        _pollError = message;
        if (before != ErrorText)
        {
            OnPropertyChanged(nameof(ErrorText));
        }
    }

    /// <summary>
    /// Updates the command error and notifies if the visible message changed.
    /// </summary>
    /// <param name="message">New command error, or null.</param>
    private void SetCommandError(string? message)
    {
        var before = ErrorText;
        _commandError = message;
        if (before != ErrorText)
        {
            OnPropertyChanged(nameof(ErrorText));
        }
    }

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for the calling property.
    /// </summary>
    /// <param name="propertyName">
    /// Filled in by the compiler from the calling member's name.
    /// </param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
