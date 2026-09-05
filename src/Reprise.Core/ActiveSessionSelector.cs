namespace Reprise.Core;

/// <summary>
/// Picks the one session Reprise should display when several players are open.
/// </summary>
/// <remarks>
/// A desktop routinely has more than one MPRIS player on the bus at once - a
/// music app, a browser tab, a video call - and only one of them belongs in
/// the panel. This type holds that choice in a single place so every platform
/// backend resolves it the same way.
/// </remarks>
public static class ActiveSessionSelector
{
    /// <summary>
    /// Chooses the session that best represents what the user is listening to.
    /// </summary>
    /// <remarks>
    /// Candidates are ranked on four keys, applied in order: playback status
    /// first, so anything actually playing outranks anything idle; then the
    /// caller's preference list; then how recently the session was observed;
    /// and finally the player id, so the result stays stable across polls
    /// instead of flickering between two otherwise identical players.
    /// </remarks>
    /// <param name="sessions">
    /// Sessions reported by the platform backend. May be empty; must not be
    /// null.
    /// </param>
    /// <param name="preferredPlayerIds">
    /// Player ids in descending priority, consulted only to break a tie
    /// between sessions sharing the same playback status. Ids not in the list
    /// rank last, and a repeated id keeps its first, highest position.
    /// Defaults to null, meaning no preference.
    /// </param>
    /// <returns>
    /// The winning session, or null when the sequence is empty.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sessions"/> is null.
    /// </exception>
    /// <example>
    /// <code>
    /// // A paused Spotify beats a paused Firefox.
    /// var active = ActiveSessionSelector.Select(sessions, ["spotify", "firefox"]);
    /// </code>
    /// </example>
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

    /// <summary>
    /// Scores a playback status by how strong a claim it has on the panel.
    /// </summary>
    /// <remarks>
    /// Kept separate from the enum's own numeric values so the display order
    /// can be tuned without silently changing the meaning of a serialised
    /// <see cref="PlaybackStatus"/>.
    /// </remarks>
    /// <param name="status">Status to score.</param>
    /// <returns>A rank from 0 to 3, where a higher number wins.</returns>
    private static int StatusRank(PlaybackStatus status) => status switch
    {
        PlaybackStatus.Playing => 3,
        PlaybackStatus.Paused => 2,
        PlaybackStatus.Stopped => 1,
        _ => 0,
    };
}
