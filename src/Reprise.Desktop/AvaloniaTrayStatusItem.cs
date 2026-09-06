using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Reprise.Desktop;

/// <summary>
/// Tray entry built on Avalonia's own <see cref="TrayIcon"/>.
/// </summary>
/// <remarks>
/// The fallback for platforms without a dedicated implementation. It can
/// show an icon, a tooltip, and a menu, but no label: Avalonia's tray has
/// no notion of text beside the icon, which is why Linux brings its own
/// D-Bus implementation instead.
/// </remarks>
public sealed class AvaloniaTrayStatusItem : IStatusItem
{
    private readonly TrayIcon _trayIcon;
    private readonly Application _application;
    private bool _disposed;

    /// <summary>
    /// Creates the tray icon with the standard menu.
    /// </summary>
    /// <param name="application">Application the icon belongs to.</param>
    public AvaloniaTrayStatusItem(Application application)
    {
        _application = application;

        var openItem = new NativeMenuItem { Header = "Reprise 열기" };
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var quitItem = new NativeMenuItem { Header = "Reprise 종료" };
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        var menu = new NativeMenu();
        menu.Add(openItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);

        using var iconStream = AssetLoader.Open(new Uri("avares://Reprise.Desktop/Assets/reprise.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            IsVisible = false,
            ToolTipText = "Reprise",
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? Activated;

    /// <inheritdoc />
    public event EventHandler? OpenRequested;

    /// <inheritdoc />
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Shows the tray icon.
    /// </summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Always true; Avalonia reports no registration outcome.</returns>
    public Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        TrayIcon.SetIcons(_application, new TrayIcons { _trayIcon });
        _trayIcon.IsVisible = true;
        return Task.FromResult(true);
    }

    /// <summary>
    /// Applies the tooltip; the label and icon are not supported here.
    /// </summary>
    /// <param name="state">Content to show.</param>
    public void Update(StatusItemState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _trayIcon.ToolTipText = state.ToolTip;
    }

    /// <summary>
    /// Removes the tray icon.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
