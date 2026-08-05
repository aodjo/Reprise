namespace Reprise.Core;

public sealed record MediaSessionSnapshot(
    string PlayerId,
    string PlayerName,
    PlaybackStatus Status,
    string Title,
    string Artist,
    string Album,
    TimeSpan? Duration,
    TimeSpan? Position,
    double? Volume,
    Uri? ArtworkUri,
    DateTimeOffset ObservedAt)
{
    public double Progress
    {
        get
        {
            if (Duration is not { Ticks: > 0 } duration || Position is null)
            {
                return 0;
            }

            return Math.Clamp(Position.Value.TotalSeconds / duration.TotalSeconds, 0, 1);
        }
    }
}
