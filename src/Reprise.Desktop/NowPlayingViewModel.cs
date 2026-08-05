using System.ComponentModel;
using System.Runtime.CompilerServices;
using Reprise.Core;

namespace Reprise.Desktop;

public sealed class NowPlayingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaSessionService _mediaSessionService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private MediaSessionSnapshot? _activeSession;
    private string _statusText = "MPRIS 플레이어를 찾는 중…";
    private string? _errorText;
    private bool _disposed;

    public NowPlayingViewModel(IMediaSessionService mediaSessionService)
    {
        _mediaSessionService = mediaSessionService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
