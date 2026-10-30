namespace Reprise.Desktop;

/// <summary>
/// Manages the current user's launch-at-login registration on the host platform.
/// </summary>
public interface IAutostartEntry
{
    /// <summary>Whether launch-at-login can be configured in this environment.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether this Reprise executable is registered for login.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Creates or removes the current user's registration.
    /// </summary>
    /// <param name="enabled">Desired login behavior.</param>
    /// <returns>Null on success, or an error suitable for Settings.</returns>
    string? SetEnabled(bool enabled);
}
