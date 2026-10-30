using System.Runtime.InteropServices;
using Reprise.Core;

namespace Reprise.Platform.Windows.Media;

/// <summary>
/// Routes shared playback requests to the exact Windows session selected by the user.
/// </summary>
internal sealed class WindowsMediaSessionService : IMediaSessionService
{
    private readonly IWindowsMediaSessionSource _source;

    /// <summary>
    /// Connects to the real Windows system media broker.
    /// </summary>
    public WindowsMediaSessionService() : this(new WindowsMediaSessionSource())
    {
    }

    /// <summary>
    /// Supplies session enumeration for deterministic routing and failure tests.
    /// </summary>
    /// <param name="source">Native session source or a test implementation.</param>
    internal WindowsMediaSessionService(IWindowsMediaSessionSource source) => _source = source;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaSessionSnapshot>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _source.GetSessionsAsync(cancellationToken);
        var snapshots = await Task.WhenAll(sessions.Select(session => ReadSafelyAsync(session, cancellationToken)));
        return snapshots.OfType<MediaSessionSnapshot>().ToArray();
    }

    /// <inheritdoc />
    public async Task SendCommandAsync(string playerId, PlaybackCommand command, CancellationToken cancellationToken = default)
    {
        var session = await FindAsync(playerId, cancellationToken);
        if (!await session.SendAsync(command, cancellationToken))
        {
            throw new InvalidOperationException("플레이어가 재생 명령을 지원하지 않거나 거부했습니다.");
        }
    }

    /// <inheritdoc />
    public async Task SeekAsync(string playerId, TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var session = await FindAsync(playerId, cancellationToken);
        if (!await session.SeekAsync(position, cancellationToken))
        {
            throw new InvalidOperationException("이 플레이어에서는 재생 위치를 변경할 수 없습니다.");
        }
    }

    /// <inheritdoc />
    public Task SetVolumeAsync(string playerId, double volume, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Windows 미디어 세션은 개별 플레이어의 음량 제어를 제공하지 않습니다."));

    /// <summary>
    /// Finds a session by its exact identifier rather than controlling the system's current session.
    /// </summary>
    /// <param name="playerId">Identifier from a previously returned snapshot.</param>
    /// <param name="cancellationToken">Cancels enumeration.</param>
    /// <returns>The matching session.</returns>
    private async Task<IWindowsMediaSession> FindAsync(string playerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var sessions = await _source.GetSessionsAsync(cancellationToken);
        return sessions.FirstOrDefault(session => session.Id == playerId)
            ?? throw new InvalidOperationException("플레이어 연결이 종료되었습니다. 다시 재생해 주세요.");
    }

    /// <summary>
    /// Isolates a disappearing or inaccessible player without hiding other sessions.
    /// </summary>
    /// <param name="session">Session to read.</param>
    /// <param name="cancellationToken">Cancels the native read.</param>
    /// <returns>The observed snapshot, or null if that session disappeared.</returns>
    private static async Task<MediaSessionSnapshot?> ReadSafelyAsync(IWindowsMediaSession session, CancellationToken cancellationToken)
    {
        try
        {
            return await session.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Reprise: media session {session.Id} unavailable (0x{exception.HResult:X8}): {exception}");
            return null;
        }
    }
}
