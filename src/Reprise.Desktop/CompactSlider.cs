using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Reprise.Desktop;

/// <summary>
/// The thin slider the panel uses for scrubbing and volume.
/// </summary>
/// <remarks>
/// Drawn by hand rather than restyled from the Fluent slider because the
/// macOS control it mirrors is a 3px track with a small round knob, and
/// matching that through a control theme would take more markup than the
/// control itself. It also reports the start and end of a drag, which the
/// panel needs to hold a seek until the pointer is released.
/// </remarks>
public sealed class CompactSlider : Control
{
    /// <summary>
    /// Thickness of the track.
    /// </summary>
    private const double TrackHeight = 3;

    /// <summary>
    /// Radius of the knob.
    /// </summary>
    private const double KnobRadius = 5.5;

    /// <summary>
    /// Lowest value the slider can express.
    /// </summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Minimum), 0);

    /// <summary>
    /// Highest value the slider can express.
    /// </summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Maximum), 1);

    /// <summary>
    /// Current value.
    /// </summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Value), 0);

    /// <summary>
    /// Brush for the unfilled part of the track.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CompactSlider, IBrush?>(nameof(TrackBrush), Brushes.Gray);

    /// <summary>
    /// Brush for the filled part of the track, up to the knob.
    /// </summary>
    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<CompactSlider, IBrush?>(nameof(FillBrush), Brushes.DodgerBlue);

    /// <summary>
    /// Brush for the knob.
    /// </summary>
    public static readonly StyledProperty<IBrush?> KnobBrushProperty =
        AvaloniaProperty.Register<CompactSlider, IBrush?>(nameof(KnobBrush), Brushes.White);

    private static readonly IBrush KnobShadowBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0, 0, 0));

    private bool _isDragging;

    /// <summary>
    /// Registers the property watchers that trigger a redraw.
    /// </summary>
    static CompactSlider()
    {
        AffectsRender<CompactSlider>(
            MinimumProperty,
            MaximumProperty,
            ValueProperty,
            TrackBrushProperty,
            FillBrushProperty,
            KnobBrushProperty,
            IsEnabledProperty);
    }

    /// <summary>
    /// Creates a slider sized for the panel.
    /// </summary>
    public CompactSlider()
    {
        Height = 16;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>
    /// Raised when the user presses on the slider, before any value change.
    /// </summary>
    public event EventHandler? DragStarted;

    /// <summary>
    /// Raised while the user drags or clicks, with the value they chose.
    /// </summary>
    public event EventHandler<double>? UserValueChanged;

    /// <summary>
    /// Raised when the user releases the slider.
    /// </summary>
    public event EventHandler? DragCompleted;

    /// <summary>
    /// Lowest value the slider can express.
    /// </summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Highest value the slider can express.
    /// </summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Current value.
    /// </summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Brush for the unfilled part of the track.
    /// </summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>
    /// Brush for the filled part of the track.
    /// </summary>
    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>
    /// Brush for the knob.
    /// </summary>
    public IBrush? KnobBrush
    {
        get => GetValue(KnobBrushProperty);
        set => SetValue(KnobBrushProperty, value);
    }

    /// <summary>
    /// Whether the user is holding the slider.
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Draws the track, its filled portion, and the knob.
    /// </summary>
    /// <remarks>
    /// The whole control is dimmed when disabled, which is how the macOS
    /// slider reads while a player cannot seek. A transparent fill under the
    /// track makes the full strip respond to the pointer, not just the three
    /// painted pixels of the track.
    /// </remarks>
    /// <param name="context">Surface to draw on.</param>
    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        context.DrawRectangle(Brushes.Transparent, null, new Rect(bounds.Size));
        var opacity = IsEnabled ? 1 : 0.45;
        using var _ = context.PushOpacity(opacity);

        var centerY = bounds.Height / 2;
        var left = KnobRadius;
        var right = Math.Max(left, bounds.Width - KnobRadius);
        var knobX = left + (right - left) * Fraction(Value);
        var trackRect = new Rect(left, centerY - TrackHeight / 2, right - left, TrackHeight);
        var fillRect = new Rect(left, centerY - TrackHeight / 2, Math.Max(0, knobX - left), TrackHeight);

        if (TrackBrush is { } track)
        {
            context.DrawRectangle(track, null, new RoundedRect(trackRect, TrackHeight / 2));
        }

        if (FillBrush is { } fill && fillRect.Width > 0)
        {
            context.DrawRectangle(fill, null, new RoundedRect(fillRect, TrackHeight / 2));
        }

        var shadowCenter = new Point(knobX, centerY + 0.5);
        context.DrawEllipse(KnobShadowBrush, null, shadowCenter, KnobRadius + 0.5, KnobRadius + 0.5);
        if (KnobBrush is { } knob)
        {
            context.DrawEllipse(knob, null, new Point(knobX, centerY), KnobRadius, KnobRadius);
        }
    }

    /// <summary>
    /// Begins a drag and moves the knob to the pointer.
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
        _isDragging = true;
        DragStarted?.Invoke(this, EventArgs.Empty);
        ApplyPointer(e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>
    /// Follows the pointer while a drag is in progress.
    /// </summary>
    /// <param name="e">Pointer event.</param>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging)
        {
            return;
        }

        ApplyPointer(e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>
    /// Ends a drag.
    /// </summary>
    /// <param name="e">Pointer event.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging)
        {
            return;
        }

        ApplyPointer(e.GetPosition(this));
        e.Pointer.Capture(null);
        FinishDrag();
        e.Handled = true;
    }

    /// <summary>
    /// Ends a drag whose pointer was taken away, so a seek is never left hanging.
    /// </summary>
    /// <param name="e">Capture event.</param>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        FinishDrag();
    }

    /// <summary>
    /// Converts a value into its fraction of the track.
    /// </summary>
    /// <param name="value">Value to place.</param>
    /// <returns>A fraction from 0 to 1.</returns>
    private double Fraction(double value)
    {
        var range = Maximum - Minimum;
        if (range <= 0 || double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp((value - Minimum) / range, 0, 1);
    }

    /// <summary>
    /// Sets the value from a pointer position and reports it.
    /// </summary>
    /// <param name="position">Pointer position relative to this control.</param>
    private void ApplyPointer(Point position)
    {
        var left = KnobRadius;
        var right = Math.Max(left + 1, Bounds.Width - KnobRadius);
        var fraction = Math.Clamp((position.X - left) / (right - left), 0, 1);
        var value = Minimum + (Maximum - Minimum) * fraction;
        Value = value;
        UserValueChanged?.Invoke(this, value);
    }

    /// <summary>
    /// Clears the drag state and notifies listeners once.
    /// </summary>
    private void FinishDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
