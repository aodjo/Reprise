namespace Reprise.Platform.Windows.Media;

/// <summary>
/// Reconciles broker wrappers, which Windows may recreate even between consecutive polls.
/// </summary>
/// <param name="ApplicationId">The media source's app identity.</param>
/// <param name="NativeIdentity">Canonical identity of the currently retained native object.</param>
internal readonly record struct WindowsSessionIdentity(string ApplicationId, nint NativeIdentity)
{
    /// <summary>
    /// Prefers native identity and uses app identity only when it is unambiguous in both enumerations.
    /// Duplicate app sessions with recreated wrappers receive new IDs, so stale commands fail closed.
    /// </summary>
    /// <param name="previous">Identities from the retained session wrappers.</param>
    /// <param name="current">Identities from the broker's current enumeration.</param>
    /// <param name="index">Current session to reconcile.</param>
    /// <returns>The previous index, or minus one when no safe match exists.</returns>
    internal static int Match(IReadOnlyList<WindowsSessionIdentity> previous,
        IReadOnlyList<WindowsSessionIdentity> current, int index)
    {
        var candidate = current[index];
        for (var old = 0; old < previous.Count; old++)
        {
            if (previous[old] == candidate)
            {
                return old;
            }
        }

        if (current.Count(value => value.ApplicationId == candidate.ApplicationId) != 1 ||
            previous.Count(value => value.ApplicationId == candidate.ApplicationId) != 1)
        {
            return -1;
        }

        for (var old = 0; old < previous.Count; old++)
        {
            if (previous[old].ApplicationId == candidate.ApplicationId)
            {
                return old;
            }
        }

        return -1;
    }
}
