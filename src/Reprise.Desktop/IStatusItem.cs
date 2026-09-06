namespace Reprise.Desktop;

/// <summary>
/// A raster icon for the tray, in the layout tray protocols expect.
/// </summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Argb">
/// Straight-alpha pixels, four bytes each in A, R, G, B order, rows top to
/// bottom.
/// </param>
public sealed record StatusItemIcon(int Width, int Height, byte[] Argb);

/// <summary>
/// Everything the tray shows for Reprise at one moment.
/// </summary>
/// <param name="Label">Text beside the icon, or empty for icon only.</param>
/// <param name="ToolTip">Text shown on hover.</param>
/// <param name="Icons">
/// The icon at one or more sizes, for the host to choose from. Empty means
/// keep the application icon.
/// </param>
public sealed record StatusItemState(
    string Label,
    string ToolTip,
    IReadOnlyList<StatusItemIcon> Icons);

/// <summary>
/// The platform's tray or status-bar entry for Reprise.
/// </summary>
/// <remarks>
/// Implemented per platform because each desktop exposes its status area
/// differently - Linux through D-Bus, and others through their own APIs.
/// Events are raised on whatever thread the platform delivers them on;
/// callers marshal to the UI thread themselves.
/// </remarks>
public interface IStatusItem : IDisposable
{
    /// <summary>
    /// Raised when the user clicks the item to bring up the panel.
    /// </summary>
    event EventHandler? Activated;

    /// <summary>
    /// Raised when the user picks the open entry from the item's menu.
    /// </summary>
    event EventHandler? OpenRequested;

    /// <summary>
    /// Raised when the user picks quit from the item's menu.
    /// </summary>
    event EventHandler? QuitRequested;

    /// <summary>
    /// Registers the item with the desktop.
    /// </summary>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns>
    /// True when a status area accepted the item; false when the desktop
    /// offers none, in which case the panel is the only way to reach Reprise.
    /// </returns>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes new content to the item.
    /// </summary>
    /// <param name="state">Label, tooltip, and icon to show.</param>
    void Update(StatusItemState state);
}
