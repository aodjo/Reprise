using System.ComponentModel;
using System.Runtime.CompilerServices;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Presentation state for the now-playing window.
/// </summary>
/// <remarks>
/// Sits between the platform's <see cref="IMediaSessionService"/> and the
/// window, turning a polled list of sessions into the single session, status
/// line, and error message the UI binds to. Keeping that translation here is
/// what lets the behaviour be exercised without a display, and what keeps the
/// window free of any knowledge of how sessions are obtained.
/// <para>
/// Not thread-safe by design: it is driven from the UI thread's refresh
/// timer, and the only concurrency it guards against is refreshes
/// overlapping.
/// </para>
/// </remarks>
public sealed class NowPlayingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaSessionService _mediaSessionService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private MediaSessionSnapshot? _activeSession;
    private string _statusText = "MPRIS 플레이어를 찾는 중…";
    private string? _errorText;
    private bool _disposed;

    /// <summary>
    /// Creates a view model over the given media session backend.
    /// </summary>
    /// <param name="mediaSessionService">
    /// Platform backend polled for sessions and used to dispatch playback
    /// commands.
    /// </param>
    /// <example>
    /// <code>
    /// var viewModel = new NowPlayingViewModel(new MprisMediaSessionService());
    /// </code>
    /// </example>
    public NowPlayingViewModel(IMediaSessionService mediaSessionService)
    {
        _mediaSessionService = mediaSessionService;
    }

    /// <summary>
    /// Raised on the calling thread whenever a bound property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The session currently on display, or null when nothing is playing.
    /// </summary>
    /// <remarks>
    /// Set only by <see cref="RefreshAsync"/>, from whichever session
    /// <see cref="ActiveSessionSelector"/> picks out of the latest poll.
    /// </remarks>
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
    /// Short line describing the playback state, shown under the header.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Message from the most recent failure, or null while things are fine.
    /// </summary>
    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (_errorText == value)
            {
                return;
            }

            _errorText = value;
            OnPropertyChanged();
        }
    }

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
            var sessions = await _mediaSessionService.GetSessionsAsync(
                _lifetime.Token);
            ActiveSession = ActiveSessionSelector.Select(sessions);
            ErrorText = null;
            StatusText = ActiveSession is null
                ? "재생 중인 MPRIS 플레이어가 없습니다."
                : ActiveSession.Status switch
                {
                    PlaybackStatus.Playing => "재생 중",
                    PlaybackStatus.Paused => "일시 정지",
                    PlaybackStatus.Stopped => "정지됨",
                    _ => "상태 확인 중",
                };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ActiveSession = null;
            StatusText = "Linux 미디어 세션에 연결할 수 없습니다.";
            ErrorText = exception.Message;
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
    /// cannot redirect the command to a different player.
    /// <para>
    /// Refreshes immediately afterwards so the transport buttons reflect the
    /// new state without waiting out the remainder of the timer interval.
    /// </para>
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
        if (_disposed)
        {
            return;
        }

        var session = ActiveSession;
        if (session is null)
        {
            return;
        }

        try
        {
            await _mediaSessionService.SendCommandAsync(
                session.PlayerId,
                command,
                _lifetime.Token);
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
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
        _lifetime.Cancel();
        _lifetime.Dispose();
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
