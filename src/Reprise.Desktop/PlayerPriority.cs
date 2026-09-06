using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// The user's ordering of player kinds, as the settings window edits it.
/// </summary>
/// <remarks>
/// Stored as a comma-separated list of kind names, the way the macOS app
/// stores its display priority, so the preference file reads the same on
/// both platforms. Kinds missing from the stored list are appended in
/// their default order, which keeps an old file valid when a kind is added.
/// </remarks>
public static class PlayerPriority
{
    /// <summary>
    /// Kinds in their default priority.
    /// </summary>
    public static readonly IReadOnlyList<PanelPlayerLogo> DefaultOrder =
    [
        PanelPlayerLogo.Spotify,
        PanelPlayerLogo.YouTubeMusic,
        PanelPlayerLogo.Generic,
    ];

    /// <summary>
    /// Name shown for a kind in the priority list.
    /// </summary>
    /// <param name="kind">Kind to name.</param>
    /// <returns>The display name.</returns>
    /// <example>
    /// <code>
    /// PlayerPriority.DisplayName(PanelPlayerLogo.Generic); // "기타 플레이어"
    /// </code>
    /// </example>
    public static string DisplayName(PanelPlayerLogo kind) => kind switch
    {
        PanelPlayerLogo.Spotify => "Spotify",
        PanelPlayerLogo.YouTubeMusic => "YouTube Music",
        _ => "기타 플레이어",
    };

    /// <summary>
    /// Parses a stored priority string.
    /// </summary>
    /// <param name="stored">Comma-separated kind names, possibly empty or stale.</param>
    /// <returns>Every kind exactly once, stored ones first in their order.</returns>
    /// <example>
    /// <code>
    /// PlayerPriority.Parse("youTubeMusic"); // YouTubeMusic, Spotify, Generic
    /// </code>
    /// </example>
    public static IReadOnlyList<PanelPlayerLogo> Parse(string? stored)
    {
        var order = new List<PanelPlayerLogo>();
        foreach (var name in (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<PanelPlayerLogo>(name, ignoreCase: true, out var kind) && !order.Contains(kind))
            {
                order.Add(kind);
            }
        }

        foreach (var kind in DefaultOrder)
        {
            if (!order.Contains(kind))
            {
                order.Add(kind);
            }
        }

        return order;
    }

    /// <summary>
    /// Serialises an ordering for storage.
    /// </summary>
    /// <param name="order">Kinds in priority order; missing kinds are appended.</param>
    /// <returns>Comma-separated kind names.</returns>
    public static string Serialize(IEnumerable<PanelPlayerLogo> order)
    {
        var complete = order.Distinct().ToList();
        complete.AddRange(DefaultOrder.Where(kind => !complete.Contains(kind)));
        return string.Join(",", complete.Select(kind => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..]));
    }

    /// <summary>
    /// Moves one kind to a new position.
    /// </summary>
    /// <param name="order">Current ordering.</param>
    /// <param name="kind">Kind to move.</param>
    /// <param name="targetIndex">Index it should end up at.</param>
    /// <returns>The reordered list.</returns>
    public static IReadOnlyList<PanelPlayerLogo> Move(
        IReadOnlyList<PanelPlayerLogo> order,
        PanelPlayerLogo kind,
        int targetIndex)
    {
        var list = order.ToList();
        var source = list.IndexOf(kind);
        if (source < 0)
        {
            return order;
        }

        list.RemoveAt(source);
        list.Insert(Math.Clamp(targetIndex, 0, list.Count), kind);
        return list;
    }

    /// <summary>
    /// Orders sessions by the user's kind priority, for the active-session choice.
    /// </summary>
    /// <remarks>
    /// <see cref="ActiveSessionSelector"/> takes player ids rather than kinds,
    /// so the sessions on the bus are sorted by their kind's rank and their ids
    /// handed over in that order.
    /// </remarks>
    /// <param name="sessions">Sessions from the latest poll.</param>
    /// <param name="order">Kind priority.</param>
    /// <returns>Player ids from most to least preferred.</returns>
    /// <example>
    /// <code>
    /// var preferred = PlayerPriority.PreferredIds(sessions, PlayerPriority.Parse(prefs.PlayerDisplayPriority));
    /// var active = ActiveSessionSelector.Select(sessions, preferred);
    /// </code>
    /// </example>
    public static IReadOnlyList<string> PreferredIds(
        IReadOnlyList<MediaSessionSnapshot> sessions,
        IReadOnlyList<PanelPlayerLogo> order)
    {
        return sessions
            .OrderBy(session => Rank(order, NowPlayingViewModel.LogoFor(session.PlayerId)))
            .ThenBy(session => session.PlayerId, StringComparer.Ordinal)
            .Select(session => session.PlayerId)
            .ToList();
    }

    /// <summary>
    /// Position of a kind in an ordering, with unknown kinds last.
    /// </summary>
    /// <param name="order">Kind priority.</param>
    /// <param name="kind">Kind to rank.</param>
    /// <returns>Zero for the most preferred kind.</returns>
    private static int Rank(IReadOnlyList<PanelPlayerLogo> order, PanelPlayerLogo kind)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == kind)
            {
                return index;
            }
        }

        return order.Count;
    }
}
