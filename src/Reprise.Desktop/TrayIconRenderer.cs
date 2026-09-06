using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Reprise.Desktop;

/// <summary>
/// Rasterises tray icons into the pixel layout tray protocols want.
/// </summary>
/// <remarks>
/// Tray hosts take raw ARGB pixmaps rather than image files, and want them
/// at panel sizes. Both the album cover and the application icon are drawn
/// through Avalonia's renderer here and read back, so the tray shows the
/// same rounded cover the panel does. Must be called on the UI thread.
/// </remarks>
public static class TrayIconRenderer
{
    /// <summary>
    /// Edge lengths rendered for every icon, covering common panel heights.
    /// </summary>
    /// <remarks>
    /// Hosts pick the pixmap nearest their icon size and scale it, so
    /// offering the sizes GNOME, KDE, and Xfce actually use keeps the cover
    /// crisp rather than resampled.
    /// </remarks>
    public static readonly int[] Sizes = [16, 22, 24, 32];

    /// <summary>
    /// Share of the pixmap the album cover fills; the rest stays clear.
    /// </summary>
    /// <remarks>
    /// A cover drawn edge to edge stands taller than the label beside it,
    /// because a panel sizes icons to the line height while text only
    /// reaches its cap height. Insetting brings the two level, the way the
    /// macOS menu bar draws an 18-point cover in a 22-point item.
    /// </remarks>
    public const double ArtworkScale = 0.82;

    private static readonly Uri ApplicationIconUri = new("avares://Reprise.Desktop/Assets/reprise.png");
    private static IReadOnlyList<StatusItemIcon>? _applicationIcons;

    /// <summary>
    /// Renders an album cover as rounded tray icons.
    /// </summary>
    /// <param name="artwork">Encoded cover image.</param>
    /// <returns>
    /// One icon per entry of <see cref="Sizes"/>, or the application icons
    /// when the data cannot be decoded.
    /// </returns>
    /// <example>
    /// <code>
    /// var icons = TrayIconRenderer.RenderArtwork(viewModel.ArtworkData!);
    /// </code>
    /// </example>
    public static IReadOnlyList<StatusItemIcon> RenderArtwork(byte[] artwork)
    {
        ArgumentNullException.ThrowIfNull(artwork);

        Bitmap cover;
        try
        {
            using var stream = new MemoryStream(artwork);
            cover = Bitmap.DecodeToWidth(stream, Sizes.Max() * 2);
        }
        catch (Exception)
        {
            return ApplicationIcons();
        }

        using (cover)
        {
            return Sizes.Select(size => Render(size, (context, bounds) =>
            {
                var target = Inset(bounds);
                using var clip = context.PushClip(new RoundedRect(target, target.Width * 0.22));
                context.DrawImage(cover, CenteredSquare(cover.PixelSize), target);
            })).ToList();
        }
    }

    /// <summary>
    /// The Reprise icon at every tray size.
    /// </summary>
    /// <remarks>
    /// Rendered once and cached, since the asset never changes.
    /// </remarks>
    /// <returns>One icon per entry of <see cref="Sizes"/>.</returns>
    public static IReadOnlyList<StatusItemIcon> ApplicationIcons()
    {
        if (_applicationIcons is not null)
        {
            return _applicationIcons;
        }

        using var stream = AssetLoader.Open(ApplicationIconUri);
        using var source = new Bitmap(stream);
        _applicationIcons = Sizes.Select(size => Render(size, (context, bounds) =>
            context.DrawImage(source, new Rect(source.Size), Inset(bounds)))).ToList();
        return _applicationIcons;
    }

    /// <summary>
    /// Renders an album cover as a PNG for consumers that decode images.
    /// </summary>
    /// <remarks>
    /// The StatusNotifierItem protocol takes raw pixels, but a shell
    /// extension loads an image far more easily than it assembles a
    /// pixmap, so the same cover is offered in both forms.
    /// </remarks>
    /// <param name="artwork">Encoded cover image, or null for the app icon.</param>
    /// <param name="size">Edge length in pixels.</param>
    /// <returns>PNG bytes, or an empty array when nothing could be drawn.</returns>
    /// <example>
    /// <code>
    /// var png = TrayIconRenderer.RenderPng(viewModel.ArtworkData, 32);
    /// </code>
    /// </example>
    public static byte[] RenderPng(byte[]? artwork, int size)
    {
        Bitmap? cover = null;
        try
        {
            if (artwork is not null)
            {
                using var source = new MemoryStream(artwork);
                cover = Bitmap.DecodeToWidth(source, size * 2);
            }
        }
        catch (Exception)
        {
            cover = null;
        }

        try
        {
            using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var context = target.CreateDrawingContext(clear: true))
            {
                var bounds = new Rect(0, 0, size, size);
                if (cover is not null)
                {
                    using var clip = context.PushClip(new RoundedRect(bounds, size * 0.22));
                    context.DrawImage(cover, CenteredSquare(cover.PixelSize), bounds);
                }
                else
                {
                    using var stream = AssetLoader.Open(ApplicationIconUri);
                    using var icon = new Bitmap(stream);
                    context.DrawImage(icon, new Rect(icon.Size), bounds);
                }
            }

            using var png = new MemoryStream();
            target.Save(png, PngBitmapEncoderOptions.Default);
            return png.ToArray();
        }
        catch (Exception)
        {
            return [];
        }
        finally
        {
            cover?.Dispose();
        }
    }

    /// <summary>
    /// Converts a bitmap's pixels into straight-alpha ARGB bytes.
    /// </summary>
    /// <remarks>
    /// Avalonia's render targets hold premultiplied BGRA; tray hosts expect
    /// straight ARGB in byte order. Both the channel swap and the
    /// un-premultiply happen here.
    /// </remarks>
    /// <param name="bitmap">Bitmap to read.</param>
    /// <returns>The icon with four bytes per pixel in A, R, G, B order.</returns>
    public static StatusItemIcon ToStatusItemIcon(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var redFirst = bitmap.Format == PixelFormats.Rgba8888;
        var premultiplied = bitmap.AlphaFormat != AlphaFormat.Unpremul;
        var argb = new byte[pixels.Length];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            var red = redFirst ? pixels[offset] : pixels[offset + 2];
            var green = pixels[offset + 1];
            var blue = redFirst ? pixels[offset + 2] : pixels[offset];
            if (premultiplied && alpha is > 0 and < 255)
            {
                red = (byte)Math.Min(255, red * 255 / alpha);
                green = (byte)Math.Min(255, green * 255 / alpha);
                blue = (byte)Math.Min(255, blue * 255 / alpha);
            }

            argb[offset] = alpha;
            argb[offset + 1] = red;
            argb[offset + 2] = green;
            argb[offset + 3] = blue;
        }

        return new StatusItemIcon(width, height, argb);
    }

    /// <summary>
    /// Draws into a square render target and reads it back.
    /// </summary>
    /// <param name="size">Edge length in pixels.</param>
    /// <param name="draw">Draws the icon into the given bounds.</param>
    /// <returns>The rendered icon.</returns>
    private static StatusItemIcon Render(int size, Action<DrawingContext, Rect> draw)
    {
        using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = target.CreateDrawingContext(clear: true))
        {
            draw(context, new Rect(0, 0, size, size));
        }

        return ToStatusItemIcon(target);
    }

    /// <summary>
    /// The area an icon is drawn into, inset by <see cref="ArtworkScale"/>.
    /// </summary>
    /// <param name="bounds">Full pixmap bounds.</param>
    /// <returns>A centred square on whole pixels.</returns>
    private static Rect Inset(Rect bounds)
    {
        var edge = Math.Round(bounds.Width * ArtworkScale);
        var offset = Math.Floor((bounds.Width - edge) / 2);
        return new Rect(offset, offset, edge, edge);
    }

    /// <summary>
    /// The largest centred square inside an image, for a uniform-to-fill crop.
    /// </summary>
    /// <param name="size">Image size in pixels.</param>
    /// <returns>The source rectangle to draw.</returns>
    private static Rect CenteredSquare(PixelSize size)
    {
        var edge = Math.Min(size.Width, size.Height);
        return new Rect((size.Width - edge) / 2.0, (size.Height - edge) / 2.0, edge, edge);
    }
}
