namespace Reprise.Core;

public static class ActiveSessionSelector
{
    public static MediaSessionSnapshot? Select(
        IEnumerable<MediaSessionSnapshot> sessions,
        IReadOnlyList<string>? preferredPlayerIds = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var priorities = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        if (preferredPlayerIds is not null)
        {
            for (var index = 0; index < preferredPlayerIds.Count; index++)
            {
                priorities.TryAdd(preferredPlayerIds[index], index);
            }
        }

        return sessions
            .OrderByDescending(session => StatusRank(session.Status))
            .ThenBy(session => priorities.GetValueOrDefault(
                session.PlayerId,
                int.MaxValue))
            .ThenByDescending(session => session.ObservedAt)
            .ThenBy(session => session.PlayerId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int StatusRank(PlaybackStatus status) => status switch
    {
        PlaybackStatus.Playing => 3,
        PlaybackStatus.Paused => 2,
        PlaybackStatus.Stopped => 1,
        _ => 0,
    };
}
