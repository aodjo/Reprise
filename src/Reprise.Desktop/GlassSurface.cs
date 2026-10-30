using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>Specular surface and Fresnel-like rim above the platform's actual refracted background.</summary>
public sealed class GlassSurface : Control
{
    private bool _dark;
    /// <summary>Fixed upper-left light source that remains stable when the pointer moves.</summary>
    private static readonly Point LightOrigin = new(0.25, 0.08);

    /// <summary>Updates surface contrast without modifying the background texture or foreground controls.</summary>
    public void SetAppearance(bool dark)
    {
        _dark = dark;
        InvalidateVisual();
    }

    /// <summary>Draws a fixed outer reflection, inner depth edge, and soft surface sheen.</summary>
    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 4 || Bounds.Height < 4) return;
        var bounds = new Rect(Bounds.Size).Deflate(0.75);
        var sheen = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(LightOrigin, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1 - LightOrigin.X * 0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(PanelPalette.WithAlpha(Colors.White, _dark ? 0.08 : 0.16), 0),
                new GradientStop(Colors.Transparent, 0.4),
                new GradientStop(PanelPalette.WithAlpha(Colors.White, 0.025), 1),
            },
        };
        var rim = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(LightOrigin, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(PanelPalette.WithAlpha(Colors.White, _dark ? 0.5 : 0.85), 0),
                new GradientStop(PanelPalette.WithAlpha(Colors.White, 0.12), 0.45),
                new GradientStop(PanelPalette.WithAlpha(Colors.Black, _dark ? 0.5 : 0.2), 0.75),
                new GradientStop(PanelPalette.WithAlpha(Colors.White, 0.3), 1),
            },
        };
        context.DrawRectangle(sheen, new Pen(rim, 1.2), bounds, NowPlayingWindow.PanelCornerRadius, NowPlayingWindow.PanelCornerRadius);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(PanelPalette.WithAlpha(Colors.White, _dark ? 0.08 : 0.2)), 0.6), bounds.Deflate(1.2), NowPlayingWindow.PanelCornerRadius - 1.2, NowPlayingWindow.PanelCornerRadius - 1.2);
    }
}
