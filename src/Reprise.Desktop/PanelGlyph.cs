using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>
/// Draws one <see cref="PanelIcons"/> geometry at a chosen size.
/// </summary>
/// <remarks>
/// The geometry is fitted to <see cref="IconSize"/> by its own bounds and
/// centred in the control, so a glyph reads at the same visual size whatever
/// box it was authored in. Disabled glyphs are dimmed the way macOS dims a
/// disabled symbol.
/// </remarks>
public class PanelGlyph : Control
{
    /// <summary>
    /// Opacity applied while the control is disabled.
    /// </summary>
    private const double DisabledOpacity = 0.35;

    /// <summary>
    /// Geometry to draw.
    /// </summary>
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<PanelGlyph, Geometry?>(nameof(Icon));

    /// <summary>
    /// Length of the glyph's longer side, in device-independent pixels.
    /// </summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<PanelGlyph, double>(nameof(IconSize), 16);

    /// <summary>
    /// Brush the glyph is filled with.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<PanelGlyph, IBrush?>(nameof(Foreground), Brushes.Black);

    /// <summary>
    /// Registers the property watchers that trigger a redraw or re-measure.
    /// </summary>
    static PanelGlyph()
    {
        AffectsRender<PanelGlyph>(IconProperty, IconSizeProperty, ForegroundProperty, IsEnabledProperty);
        AffectsMeasure<PanelGlyph>(IconSizeProperty);
    }

    /// <summary>
    /// Geometry to draw.
    /// </summary>
    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Length of the glyph's longer side.
    /// </summary>
    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>
    /// Brush the glyph is filled with.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Brush actually used when drawing, which subclasses may vary by state.
    /// </summary>
    protected virtual IBrush? EffectiveForeground => Foreground;

    /// <summary>
    /// Draws the glyph centred and scaled to <see cref="IconSize"/>.
    /// </summary>
    /// <remarks>
    /// A transparent fill is laid down over the whole control first, since
    /// Avalonia hit-tests painted content rather than layout bounds: without
    /// it a click in the hollow centre of an outlined glyph, such as the
    /// gear, would fall through to whatever lies beneath.
    /// </remarks>
    /// <param name="context">Surface to draw on.</param>
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (Icon is not { } icon || EffectiveForeground is not { } brush)
        {
            return;
        }

        var source = icon.Bounds;
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= 0)
        {
            return;
        }

        var scale = IconSize / longest;
        var drawnWidth = source.Width * scale;
        var drawnHeight = source.Height * scale;
        var offsetX = (Bounds.Width - drawnWidth) / 2;
        var offsetY = (Bounds.Height - drawnHeight) / 2;
        var transform = Matrix.CreateTranslation(-source.X, -source.Y)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(offsetX, offsetY);

        using var opacity = context.PushOpacity(IsEnabled ? 1 : DisabledOpacity);
        using var pushed = context.PushTransform(transform);
        context.DrawGeometry(brush, null, icon);
    }

    /// <summary>
    /// Reports the glyph's box when no explicit size was given.
    /// </summary>
    /// <param name="availableSize">Space offered by the parent.</param>
    /// <returns>A square of <see cref="IconSize"/> per side.</returns>
    protected override Size MeasureOverride(Size availableSize) => new(IconSize, IconSize);
}

/// <summary>
/// A glyph that behaves as a chrome-less button.
/// </summary>
/// <remarks>
/// Mirrors the plain and compact button styles of the macOS panel: no
/// background or border, a slight shrink and a dimmer fill while pressed,
/// and a click that fires only if the pointer is released over the glyph.
/// </remarks>
public sealed class PanelIconButton : PanelGlyph
{
    /// <summary>
    /// Scale applied while pressed.
    /// </summary>
    private const double PressedScale = 0.94;

    /// <summary>
    /// Brush used while pressed. Falls back to <see cref="PanelGlyph.Foreground"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> PressedForegroundProperty =
        AvaloniaProperty.Register<PanelIconButton, IBrush?>(nameof(PressedForeground));

    private bool _isPressed;

    /// <summary>
    /// Registers the property watchers that trigger a redraw.
    /// </summary>
    static PanelIconButton()
    {
        AffectsRender<PanelIconButton>(PressedForegroundProperty);
    }

    /// <summary>
    /// Creates the button with an arrow cursor, as macOS shows over its buttons.
    /// </summary>
    public PanelIconButton()
    {
        Cursor = new Cursor(StandardCursorType.Arrow);
        RenderTransformOrigin = RelativePoint.Center;
        Focusable = false;
    }

    /// <summary>
    /// Raised when the pointer is released over the button.
    /// </summary>
    public event EventHandler? Click;

    /// <summary>
    /// Brush used while pressed.
    /// </summary>
    public IBrush? PressedForeground
    {
        get => GetValue(PressedForegroundProperty);
        set => SetValue(PressedForegroundProperty, value);
    }

    /// <summary>
    /// The pressed brush while held, otherwise the resting brush.
    /// </summary>
    protected override IBrush? EffectiveForeground =>
        _isPressed ? PressedForeground ?? Foreground : Foreground;

    /// <summary>
    /// Enters the pressed state and captures the pointer.
    /// </summary>
    /// <param name="e">Pointer event.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(this);
        SetPressed(true);
        e.Handled = true;
    }

    /// <summary>
    /// Leaves the pressed state and clicks if released over the button.
    /// </summary>
    /// <param name="e">Pointer event.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isPressed)
        {
            return;
        }

        var inside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        e.Pointer.Capture(null);
        SetPressed(false);
        e.Handled = true;
        if (inside)
        {
            Click?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Leaves the pressed state without clicking when capture is taken away.
    /// </summary>
    /// <param name="e">Capture event.</param>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        SetPressed(false);
    }

    /// <summary>
    /// Applies or removes the pressed shrink and redraws.
    /// </summary>
    /// <param name="pressed">Whether the button is held.</param>
    private void SetPressed(bool pressed)
    {
        if (_isPressed == pressed)
        {
            return;
        }

        _isPressed = pressed;
        RenderTransform = pressed ? new ScaleTransform(PressedScale, PressedScale) : null;
        InvalidateVisual();
    }
}
