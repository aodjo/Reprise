using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Media.Control;
using Reprise.Core;

namespace Reprise.Platform.Windows.Media;

/// <summary>
/// Adapts the Windows global media-session manager while preserving distinct session identities.
/// </summary>
internal sealed class WindowsMediaSessionSource : IWindowsMediaSessionSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private List<WindowsNativeMediaSession> _sessions = [];
    private long _nextId;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IWindowsMediaSession>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
            var nativeSessions = _manager.GetSessions().ToArray();
            var identities = nativeSessions.Select(native => new WindowsSessionIdentity(native.SourceAppUserModelId,
                WindowsNativeMediaSession.Identity(native))).ToArray();
            var previous = _sessions.Select(session => session.SessionIdentity).ToArray();
            var current = new List<WindowsNativeMediaSession>();
            for (var index = 0; index < nativeSessions.Length; index++)
            {
                var native = nativeSessions[index];
                var match = WindowsSessionIdentity.Match(previous, identities, index);
                var session = match < 0 ? new WindowsNativeMediaSession(native, ++_nextId) : _sessions[match];
                session.Refresh(native);
                current.Add(session);
            }

            _sessions = current;
            return current.Cast<IWindowsMediaSession>().ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Reads metadata, artwork, and timeline state directly from one GSMTC session.
/// </summary>
internal sealed class WindowsNativeMediaSession : IWindowsMediaSession
{
    private GlobalSystemMediaTransportControlsSession _session;
    private string? _artworkKey;
    private Uri? _artwork;

    /// <summary>
    /// Wraps a native session with an identifier that remains stable across polls.
    /// </summary>
    /// <param name="session">Native session object.</param>
    /// <param name="sequence">Process-local sequence distinguishing sessions from the same app.</param>
    public WindowsNativeMediaSession(GlobalSystemMediaTransportControlsSession session, long sequence)
    {
        _session = session;
        Id = $"windows:{session.SourceAppUserModelId}:{sequence}";
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>
    /// Captures the app and native identity used to reconcile the next broker enumeration.
    /// </summary>
    public WindowsSessionIdentity SessionIdentity => new(_session.SourceAppUserModelId, Identity(_session));

    /// <summary>Replaces a recreated native wrapper while keeping the logical player identifier.</summary>
    /// <param name="session">Current native wrapper for the matched player.</param>
    public void Refresh(GlobalSystemMediaTransportControlsSession session) => _session = session;

    /// <summary>
    /// Queries canonical IUnknown identity rather than comparing potentially different interface pointers.
    /// </summary>
    /// <param name="session">A live WinRT session wrapper.</param>
    /// <returns>The object's identity pointer, kept alive by its session wrapper.</returns>
    internal static nint Identity(GlobalSystemMediaTransportControlsSession session)
    {
        var unknown = new Guid("00000000-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(((WinRT.IWinRTObject)session).NativeObject.ThisPtr,
            in unknown, out var identity));
        Marshal.Release(identity);
        return identity;
    }

    /// <inheritdoc />
    public async Task<MediaSessionSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var properties = await _session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
        var playback = _session.GetPlaybackInfo();
        var timeline = _session.GetTimelineProperties();
        var now = DateTimeOffset.UtcNow;
        var status = playback.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
            _ => PlaybackStatus.Stopped,
        };
        var (duration, position) = WindowsPlaybackTimeline.Observe(timeline.StartTime, timeline.EndTime,
            timeline.Position, timeline.LastUpdatedTime, now, status == PlaybackStatus.Playing, playback.PlaybackRate ?? 1);
        var artworkKey = $"{properties.Title}\n{properties.Artist}\n{properties.AlbumTitle}";
        if (artworkKey != _artworkKey)
        {
            _artwork = await ReadArtworkAsync(properties, cancellationToken);
            _artworkKey = _artwork is null ? null : artworkKey;
        }

        return new MediaSessionSnapshot(Id, WindowsPlayerNames.DisplayName(_session.SourceAppUserModelId), status,
            properties.Title ?? string.Empty, properties.Artist ?? string.Empty, properties.AlbumTitle ?? string.Empty,
            duration, position, await Task.Run(ReadVolume, cancellationToken), _artwork, now);
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(PlaybackCommand command, CancellationToken cancellationToken)
    {
        var playback = _session.GetPlaybackInfo();
        var controls = playback.Controls;
        return command switch
        {
            PlaybackCommand.PlayPause or PlaybackCommand.Pause => await WindowsPlaybackControl.SendAsync(command,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayEnabled, controls.IsPauseEnabled, controls.IsPlayPauseToggleEnabled,
                () => _session.TryPlayAsync().AsTask(cancellationToken),
                () => _session.TryPauseAsync().AsTask(cancellationToken),
                () => _session.TryTogglePlayPauseAsync().AsTask(cancellationToken)),
            PlaybackCommand.Previous => controls.IsPreviousEnabled && await _session.TrySkipPreviousAsync().AsTask(cancellationToken),
            PlaybackCommand.Next => controls.IsNextEnabled && await _session.TrySkipNextAsync().AsTask(cancellationToken),
            _ => false,
        };
    }

    /// <inheritdoc />
    public async Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        if (!_session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled)
        {
            return false;
        }

        var timeline = _session.GetTimelineProperties();
        var absolute = timeline.StartTime + position;
        var minimum = timeline.MinSeekTime > timeline.StartTime ? timeline.MinSeekTime : timeline.StartTime;
        var maximum = timeline.MaxSeekTime > minimum ? timeline.MaxSeekTime : timeline.EndTime;
        if (maximum < minimum)
        {
            return false;
        }

        return await _session.TryChangePlaybackPositionAsync(Math.Clamp(absolute.Ticks, minimum.Ticks, maximum.Ticks))
            .AsTask(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        var appId = _session.SourceAppUserModelId;
        return Task.Run(() => WindowsApplicationVolume.Set(appId, volume), cancellationToken);
    }

    /// <summary>Reads the dedicated local player's own Core Audio sessions without affecting the output master level.</summary>
    private double? ReadVolume() => WindowsApplicationVolume.Read(_session.SourceAppUserModelId);

    /// <summary>
    /// Stores the native thumbnail under its content hash without exceeding URI length limits.
    /// </summary>
    /// <param name="properties">Metadata containing the current thumbnail reference.</param>
    /// <param name="cancellationToken">Cancels native I/O.</param>
    /// <returns>The encoded artwork, or null when unavailable.</returns>
    private static async Task<Uri?> ReadArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties,
        CancellationToken cancellationToken)
    {
        if (properties.Thumbnail is null)
        {
            return null;
        }

        try
        {
            using var source = await properties.Thumbnail.OpenReadAsync().AsTask(cancellationToken);
            if (source.Size > 16 * 1024 * 1024)
            {
                return null;
            }

            using var input = source.AsStreamForRead();
            using var bytes = new MemoryStream();
            await input.CopyToAsync(bytes, cancellationToken);
            var content = bytes.ToArray();
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Reprise", "cache", "artwork");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(content)) + ".img");
            if (!File.Exists(path))
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, content, cancellationToken);
                    File.Move(temporary, path, true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }

            return new Uri(path);
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
