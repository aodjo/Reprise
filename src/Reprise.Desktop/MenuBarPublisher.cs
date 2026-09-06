namespace Reprise.Desktop;

/// <summary>
/// Everything a shell extension needs to draw Reprise in the top bar.
/// </summary>
/// <remarks>
/// Richer than <see cref="StatusItemState"/> on purpose. A tray host is
/// handed a finished label because that is all the protocol carries, while
/// an extension draws the item itself and wants the raw material: the whole
/// text, the cover as an image, and the user's scrolling settings, so it
/// can animate smoothly the way the macOS menu bar does.
/// </remarks>
/// <param name="Text">The full line to show, never shortened.</param>
/// <param name="ToolTip">Text for the hover tooltip.</param>
/// <param name="IconPng">Album cover as PNG bytes, or empty for none.</param>
/// <param name="IsRunning">Whether a player is being followed.</param>
/// <param name="IsPlaying">Whether that player is playing right now.</param>
/// <param name="ScrollsText">Whether the user wants long text to scroll.</param>
/// <param name="PointsPerSecond">Scrolling speed the user chose.</param>
/// <param name="MaxWidthChars">
/// Width the text should be held to, in characters; the extension converts
/// it to pixels with its own font.
/// </param>
public sealed record MenuBarState(
    string Text,
    string ToolTip,
    byte[] IconPng,
    bool IsRunning,
    bool IsPlaying,
    bool ScrollsText,
    double PointsPerSecond,
    int MaxWidthChars);

/// <summary>
/// Publishes <see cref="MenuBarState"/> to whatever draws the top bar.
/// </summary>
/// <remarks>
/// Implemented per platform, since the transport is platform-specific:
/// D-Bus on Linux, and nothing at all where the application draws its own
/// menu bar item.
/// </remarks>
public interface IMenuBarPublisher : IDisposable
{
    /// <summary>
    /// Raised when the shell asks Reprise to show its panel.
    /// </summary>
    event EventHandler? Activated;

    /// <summary>
    /// Starts accepting connections from the shell.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start-up.</param>
    /// <returns>A task that completes once the service is reachable.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes new content.
    /// </summary>
    /// <param name="state">What the top bar should show.</param>
    void Publish(MenuBarState state);
}
