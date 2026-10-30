using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;

namespace Reprise.Desktop;

/// <summary>
/// Tray entry built on Avalonia's own <see cref="TrayIcon"/>.
/// </summary>
/// <remarks>
/// The fallback for platforms without a dedicated implementation. It shows
/// an icon and a tooltip, but no label: Avalonia's tray has no notion of
/// text beside the icon, which is why Linux brings its own D-Bus
/// implementation instead. Like that one it offers no menu, so a click
/// opens the panel directly.
/// </remarks>
public sealed class AvaloniaTrayStatusItem : IStatusItem
{
    private readonly TrayIcon _trayIcon;
    private readonly Application _application;
    private bool _disposed;
    private StatusItemIcon? _lastIcon;

    /// <summary>
    /// Creates the tray icon.
    /// </summary>
    /// <param name="application">Application the icon belongs to.</param>
    public AvaloniaTrayStatusItem(Application application)
    {
        _application = application;

        using var iconStream = AssetLoader.Open(new Uri("avares://Reprise.Desktop/Assets/reprise.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            IsVisible = false,
            ToolTipText = "Reprise",
        };
        _trayIcon.Clicked += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? Activated;

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
    /// Applies the tooltip and native icon; text beside the icon is desktop-specific.
    /// </summary>
    /// <param name="state">Content to show.</param>
    public void Update(StatusItemState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _trayIcon.ToolTipText = state.ToolTip;
        var icon = state.Icons.OrderByDescending(value => value.Width).FirstOrDefault();
        if (icon is not null && !ReferenceEquals(icon, _lastIcon))
        {
            using var bitmap = new WriteableBitmap(new PixelSize(icon.Width, icon.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var pixels = bitmap.Lock())
            {
                var row = new byte[icon.Width * 4];
                for (var y = 0; y < icon.Height; y++)
                {
                    for (var x = 0; x < icon.Width; x++)
                    {
                        var offset = (y * icon.Width + x) * 4;
                        row[x * 4] = icon.Argb[offset + 3];
                        row[x * 4 + 1] = icon.Argb[offset + 2];
                        row[x * 4 + 2] = icon.Argb[offset + 1];
                        row[x * 4 + 3] = icon.Argb[offset];
                    }

                    Marshal.Copy(row, 0, pixels.Address + y * pixels.RowBytes, row.Length);
                }
            }

            _trayIcon.Icon = new WindowIcon(bitmap);
            _lastIcon = icon;
        }
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
