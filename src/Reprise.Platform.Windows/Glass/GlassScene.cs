using System.Numerics;
using System.Runtime.InteropServices;

namespace Reprise.Platform.Windows.Glass;

/// <summary>Live DWM lens that refracts only windows below the player without copying desktop pixels into application memory.</summary>
internal sealed class GlassScene : IDisposable
{
    private readonly nint _parent;
    private readonly nint _foreground;
    private readonly Vector2 _size;
    private readonly int _sourceMargin;
    private readonly List<OpticalRegion> _regions = [];
    private readonly List<SourceWindow> _sources = [];
    private GlassNativeWindow.Rectangle _lastBounds;
    private bool _disposed;
    /// <summary>Small center magnification that gives the material optical depth.</summary>
    internal const float Magnification = 1.015f;
    /// <summary>Gaussian standard deviation in DIP for strong, full-surface frosting.</summary>
    private const float ScatteringSigma = 14;
    /// <summary>Number of evenly distributed Gaussian samples, avoiding the visible comb pattern of a coarse rectangular kernel.</summary>
    private const int ScatteringSamples = 128;
    /// <summary>Small residual bevel contribution over the frosted background, avoiding clear strips at the edges.</summary>
    private const byte BevelOpacity = 20;
    /// <summary>Number of independently refracted regions.</summary>
    internal int RegionCount => _regions.Count;
    /// <summary>Number of live source windows currently contributing to the lens.</summary>
    internal int SourceCount => _sources.Count(source => source.Thumbnails.Length > 0 && source.Thumbnails[0] != 0);

    /// <summary>Builds the optical mesh; DWM thumbnails provide live source surfaces without capture exclusions or injections.</summary>
    internal GlassScene(nint parent, nint foreground, Vector2 size, float scale)
    {
        _parent = parent;
        _foreground = foreground;
        _size = size;
        _sourceMargin = (int)MathF.Ceiling((3 * ScatteringSigma + GlassOptics.Bevel + 16) * scale);
        AddScatteredBackground(scale, size);
        /// Four subdivisions limit sampling steps to four DIP along the bevel.
        const int steps = 4;
        var bevel = Math.Min(GlassOptics.Bevel * scale, Math.Min(size.X, size.Y) / 2);
        var step = bevel / steps;
        for (var index = 0; index < steps; index++)
        {
            var start = index * step;
            AddRegion(scale, bevel, start, size.X - bevel, start + step, opacity: BevelOpacity);
            AddRegion(scale, bevel, size.Y - start - step, size.X - bevel, size.Y - start, opacity: BevelOpacity);
            AddRegion(scale, start, bevel, start + step, size.Y - bevel, opacity: BevelOpacity);
            AddRegion(scale, size.X - start - step, bevel, size.X - start, size.Y - bevel, opacity: BevelOpacity);
            for (var second = 0; second < steps; second++)
            foreach (var right in new[] { false, true })
            foreach (var bottom in new[] { false, true })
            {
                var left = right ? size.X - start - step : start;
                var top = bottom ? size.Y - second * step - step : second * step;
                AddRegion(scale, left, top, left + step, top + step, opacity: BevelOpacity);
            }
        }
    }

    /// <summary>Stores one clipped region and its Snell-law source displacement.</summary>
    private void AddRegion(float scale, float left, float top, float right, float bottom,
        Vector2 sourceOffset = default, byte opacity = 255)
    {
        var x = (int)MathF.Round(left);
        var y = (int)MathF.Round(top);
        var width = (int)MathF.Round(right) - x;
        var height = (int)MathF.Round(bottom) - y;
        if (width <= 0 || height <= 0) return;
        var displacement = GlassOptics.Displacement(new Vector2(x + width / 2f, y + height / 2f) / scale, _size / scale, GlassOptics.Radius) * scale;
        _regions.Add(new(x, y, width, height, displacement + sourceOffset, opacity));
    }

    /// <summary>Frosts the whole live lens with Gaussian-distributed DWM samples, including edge and corner backgrounds without repeated grid artifacts.</summary>
    private void AddScatteredBackground(float scale, Vector2 size)
    {
        // Equal-mass radial quantiles and the golden angle sample a truncated Gaussian without a repeating spatial lattice.
        var goldenAngle = MathF.PI * (3 - MathF.Sqrt(5));
        var retainedMass = 1 - MathF.Exp(-4.5f);
        for (var index = 0; index < ScatteringSamples; index++)
        {
            var quantile = (index + 0.5f) / ScatteringSamples;
            var radius = ScatteringSigma * MathF.Sqrt(-2 * MathF.Log(1 - quantile * retainedMass));
            var angle = index * goldenAngle;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius * scale);
            // Cumulative alpha normalizes equal-weight samples while keeping live pixels entirely inside DWM.
            AddRegion(scale, 0, 0, size.X, size.Y, offset,
                (byte)Math.Round(255.0 / (index + 1)));
        }
    }

    /// <summary>Updates source geometry and stacking; connected window content itself refreshes automatically inside DWM.</summary>
    internal void Refresh(GlassNativeWindow.Rectangle bounds)
    {
        var windows = FindSources(bounds);
        var replaced = !_sources.Select(source => source.Handle).SequenceEqual(windows.Select(source => source.Handle));
        if (replaced)
        {
            ReleaseSources();
            foreach (var window in windows)
            {
                var thumbnails = new nint[_regions.Count];
                var source = new SourceWindow(window.Handle, window.Bounds, thumbnails);
                _sources.Add(source);
                for (var index = 0; index < thumbnails.Length; index++)
                {
                    // A source may disappear between enumeration and registration. Hide incomplete relationships on the next refresh.
                    if (DwmRegisterThumbnail(_parent, window.Handle, out thumbnails[index]) < 0) break;
                }
            }
        }
        for (var sourceIndex = 0; sourceIndex < _sources.Count; sourceIndex++)
        {
            var source = _sources[sourceIndex];
            var rectangle = windows[sourceIndex].Bounds;
            if (!replaced && rectangle.Equals(source.Bounds) && bounds.Equals(_lastBounds)) continue;
            if (source.Thumbnails.Length == 0 || source.Thumbnails[0] == 0 || DwmQueryThumbnailSourceSize(source.Thumbnails[0], out var sourceSize) < 0) continue;
            for (var regionIndex = 0; regionIndex < _regions.Count; regionIndex++)
            {
                var thumbnail = source.Thumbnails[regionIndex];
                if (thumbnail == 0) continue;
                var properties = Properties(_regions[regionIndex], bounds, rectangle, sourceSize);
                // Source destruction is transient and must not crash the player.
                _ = DwmUpdateThumbnailProperties(thumbnail, ref properties);
            }
            _sources[sourceIndex] = source with { Bounds = rectangle };
        }
        _lastBounds = bounds;
    }

    /// <summary>Finds visible, uncloaked windows behind the player in bottom-to-top desktop order.</summary>
    private List<(nint Handle, GlassNativeWindow.Rectangle Bounds)> FindSources(GlassNativeWindow.Rectangle bounds)
    {
        var result = new List<(nint, GlassNativeWindow.Rectangle)>();
        var seen = new HashSet<nint>();
        for (var handle = GetWindow(_foreground, 2); handle != 0 && seen.Add(handle); handle = GetWindow(handle, 2))
        {
            if (handle == _parent || !IsWindowVisible(handle) || IsIconic(handle)) continue;
            if (DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) >= 0 && cloaked != 0) continue;
            if (!GetWindowRect(handle, out var source)) continue;
            if (source.Right <= bounds.Left - _sourceMargin || source.Left >= bounds.Right + _sourceMargin || source.Bottom <= bounds.Top - _sourceMargin || source.Top >= bounds.Bottom + _sourceMargin) continue;
            if (source.Right <= source.Left || source.Bottom <= source.Top) continue;
            result.Add((handle, source));
        }
        result.Reverse();
        return result;
    }

    /// <summary>Clips a region against one source window and maps both rectangles through the actual optical transform.</summary>
    private ThumbnailProperties Properties(OpticalRegion region, GlassNativeWindow.Rectangle bounds, GlassNativeWindow.Rectangle source, NativeSize sourceSize)
    {
        var origin = GlassOptics.SourcePosition(new(region.Left, region.Top), _size, region.Displacement) + new Vector2(bounds.Left, bounds.Top);
        var left = Math.Max(source.Left, origin.X);
        var top = Math.Max(source.Top, origin.Y);
        var right = Math.Min(source.Right, origin.X + region.Width / Magnification);
        var bottom = Math.Min(source.Bottom, origin.Y + region.Height / Magnification);
        var value = new ThumbnailProperties { Flags = 0x1f, Opacity = region.Opacity, Visible = right > left && bottom > top ? 1 : 0 };
        if (value.Visible == 0) return value;
        var ratioX = sourceSize.Width / (float)(source.Right - source.Left);
        var ratioY = sourceSize.Height / (float)(source.Bottom - source.Top);
        value.Source = new()
        {
            Left = (int)MathF.Round((left - source.Left) * ratioX), Top = (int)MathF.Round((top - source.Top) * ratioY),
            Right = (int)MathF.Round((right - source.Left) * ratioX), Bottom = (int)MathF.Round((bottom - source.Top) * ratioY),
        };
        value.Destination = new()
        {
            Left = region.Left + (int)MathF.Round((left - origin.X) * Magnification),
            Top = region.Top + (int)MathF.Round((top - origin.Y) * Magnification),
            Right = region.Left + (int)MathF.Round((right - origin.X) * Magnification),
            Bottom = region.Top + (int)MathF.Round((bottom - origin.Y) * Magnification),
        };
        return value;
    }

    /// <summary>Disconnects all live source windows before replacing the scene or hiding the player.</summary>
    private void ReleaseSources()
    {
        foreach (var source in _sources)
        foreach (var thumbnail in source.Thumbnails)
            if (thumbnail != 0) _ = DwmUnregisterThumbnail(thumbnail);
        _sources.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseSources();
    }

    /// <summary>One fixed lens region in physical pixels.</summary>
    private sealed record OpticalRegion(int Left, int Top, int Width, int Height, Vector2 Displacement, byte Opacity);
    /// <summary>Live source and its per-region DWM relationships, registered in stacking order.</summary>
    private sealed record SourceWindow(nint Handle, GlassNativeWindow.Rectangle Bounds, nint[] Thumbnails);
    /// <summary>Physical source dimensions reported by DWM.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }
    /// <summary>Packed native DWM thumbnail layout, including Win32 BOOL fields.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ThumbnailProperties
    {
        public uint Flags;
        public GlassNativeWindow.Rectangle Destination, Source;
        public byte Opacity;
        public int Visible, ClientOnly;
    }
    /// <summary>Finds the next top-level window without modifying another application's HWND.</summary>
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);
    /// <summary>Filters hidden windows.</summary>
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);
    /// <summary>Filters minimized windows whose thumbnail could contain stale content.</summary>
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);
    /// <summary>Reads the source's screen rectangle.</summary>
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out GlassNativeWindow.Rectangle bounds);
    /// <summary>Checks whether a window belongs to another virtual desktop.</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint handle, uint attribute, out int value, int size);
    /// <summary>Connects a live source to only our owned background target.</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    /// <summary>Reads source dimensions without retrieving pixel data.</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out NativeSize size);
    /// <summary>Applies the clipped optical mapping for one source-region pair.</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    /// <summary>Disconnects only a relationship created by this scene.</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);
}
