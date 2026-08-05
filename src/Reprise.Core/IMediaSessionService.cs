namespace Reprise.Core;

public interface IMediaSessionService
{
    Task<IReadOnlyList<MediaSessionSnapshot>> GetSessionsAsync(
        CancellationToken cancellationToken = default);

    Task SendCommandAsync(
        string playerId,
        PlaybackCommand command,
        CancellationToken cancellationToken = default);
}
