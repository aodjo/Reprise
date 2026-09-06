using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Reprise.Desktop;

/// <summary>
/// Single-line track title that scrolls when it does not fit.
/// </summary>
/// <remarks>
/// A port of the macOS panel's title marquee. The title is drawn twice, a
/// fixed gap apart, and the pair slides left until the second copy reaches
/// where the first began, at which point the cycle restarts invisibly. A
/// fade at the trailing edge hides the cut. Titles that fit are drawn once
/// and never move.
/// <para>
/// Drawn directly rather than composed from text blocks so the scrolling
/// offset can be snapped to whole device pixels each frame, which is what
/// keeps small text from shimmering as it moves.
/// </para>
/// </remarks>
public sealed class PanelTitleMarquee : Control
{
    /// <summary>
    /// Space between the end of one copy of the title and the start of the next.
    /// </summary>
    private const double TitleGap = 24;

    /// <summary>
    /// Width of the fade that hides the trailing edge of a scrolling title.
    /// </summary>
    private const double FadeWidth = 14;

    /// <summary>
    /// How long a title rests before scrolling on its own.
    /// </summary>
    private static readonly TimeSpan InitialPause = TimeSpan.FromSeconds(1.4);

    /// <summary>
    /// How long a title rests before scrolling in response to a hover.
    /// </summary>
    private static readonly TimeSpan HoverInitialPause = TimeSpan.FromSeconds(0.25);

    private static readonly Typeface TitleTypeface = new(
        PanelTypography.Family,
        FontStyle.Normal,
        FontWeight.SemiBold);

    /// <summary>
    /// Text to display.
    /// </summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PanelTitleMarquee, string>(nameof(Title), string.Empty);

    /// <summary>
    /// Brush the title is drawn with.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<PanelTitleMarquee, IBrush?>(nameof(Foreground), Brushes.Black);

    /// <summary>
    /// Whether an overlong title scrolls unprompted, or only while hovered.
    /// </summary>
    public static readonly StyledProperty<bool> AutomaticallyScrollsProperty =
        AvaloniaProperty.Register<PanelTitleMarquee, bool>(nameof(AutomaticallyScrolls), true);

    /// <summary>
    /// Scrolling speed in device-independent pixels per second.
    /// </summary>
    public static readonly StyledProperty<double> PointsPerSecondProperty =
        AvaloniaProperty.Register<PanelTitleMarquee, double>(nameof(PointsPerSecond), 30);

    /// <summary>
    /// Font size of the title.
    /// </summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<PanelTitleMarquee, double>(nameof(FontSize), PanelTypography.Title);

    private readonly DispatcherTimer _frameTimer;
    private DateTimeOffset _scrollStart;
    private bool _isHovering;
    private bool _isScrolling;

    /// <summary>
    /// Registers the property watchers that trigger a redraw.
    /// </summary>
    static PanelTitleMarquee()
    {
        AffectsRender<PanelTitleMarquee>(
            TitleProperty,
            ForegroundProperty,
            AutomaticallyScrollsProperty,
            PointsPerSecondProperty,
            FontSizeProperty);
        AffectsMeasure<PanelTitleMarquee>(TitleProperty, FontSizeProperty);
    }

    /// <summary>
    /// Creates the marquee with its frame timer stopped.
    /// </summary>
    public PanelTitleMarquee()
    {
        _frameTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            (_, _) => InvalidateVisual());
        ClipToBounds = true;
    }

    /// <summary>
    /// Text to display.
    /// </summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Brush the title is drawn with.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Whether an overlong title scrolls unprompted, or only while hovered.
    /// </summary>
    public bool AutomaticallyScrolls
    {
        get => GetValue(AutomaticallyScrollsProperty);
        set => SetValue(AutomaticallyScrollsProperty, value);
    }

    /// <summary>
    /// Scrolling speed in device-independent pixels per second.
    /// </summary>
    public double PointsPerSecond
    {
        get => GetValue(PointsPerSecondProperty);
        set => SetValue(PointsPerSecondProperty, value);
    }

    /// <summary>
    /// Font size of the title.
    /// </summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Sends a scrolling title back to its beginning.
    /// </summary>
    /// <remarks>
    /// Called when the panel is summoned, so the user reads a long title
    /// from the start rather than joining it mid-scroll.
    /// </remarks>
    /// <example>
    /// <code>
    /// titleMarquee.RestartScroll();
    /// </code>
    /// </example>
    public void RestartScroll()
    {
        StopScrolling();
        InvalidateVisual();
    }

    /// <summary>
    /// Draws the title, scrolling it when it overflows and scrolling is wanted.
    /// </summary>
    /// <remarks>
    /// Also owns the frame timer: it is started here when a scroll is due
    /// and stopped as soon as the title fits or scrolling is no longer
    /// wanted, so an idle panel costs no frames. The transparent fill makes
    /// the whole line hoverable, which hover-to-scroll relies on.
    /// </remarks>
    /// <param name="context">Surface to draw on.</param>
    public override void Render(DrawingContext context)
    {
        var title = Title;
        var bounds = Bounds;
        context.DrawRectangle(Brushes.Transparent, null, new Rect(bounds.Size));
        if (string.IsNullOrEmpty(title) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            StopScrolling();
            return;
        }

        var text = CreateText(title);
        var textWidth = Math.Ceiling(text.Width);
        var y = Math.Round((bounds.Height - text.Height) / 2);
        var overflows = textWidth > bounds.Width;
        var wantsScroll = overflows && (AutomaticallyScrolls || _isHovering);

        if (!wantsScroll)
        {
            StopScrolling();
            if (overflows)
            {
                using var fade = PushFade(context, bounds);
                context.DrawText(text, new Point(0, y));
            }
            else
            {
                context.DrawText(text, new Point(0, y));
            }

            return;
        }

        if (!_isScrolling)
        {
            StartScrolling();
        }

        var distance = textWidth + TitleGap;
        var offset = ScrollOffset(distance);
        using (PushFade(context, bounds))
        {
            context.DrawText(text, new Point(offset, y));
            context.DrawText(text, new Point(offset + distance, y));
        }
    }

    /// <summary>
    /// Reports the natural height of one line and no minimum width.
    /// </summary>
    /// <remarks>
    /// Width is left to the parent so the marquee fills whatever the layout
    /// gives it and scrolls relative to that, exactly like the macOS view.
    /// </remarks>
    /// <param name="availableSize">Space offered by the parent.</param>
    /// <returns>The line height, and zero width.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var text = CreateText(string.IsNullOrEmpty(Title) ? "M" : Title);
        return new Size(0, Math.Ceiling(text.Height));
    }

    /// <summary>
    /// Starts a hover scroll when automatic scrolling is off.
    /// </summary>
    /// <param name="e">Pointer event.</param>
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (AutomaticallyScrolls)
        {
            return;
        }

        _isHovering = true;
        StopScrolling();
        InvalidateVisual();
    }

    /// <summary>
    /// Ends a hover scroll and returns the title to its start.
    /// </summary>
    /// <param name="e">Pointer event.</param>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isHovering)
        {
            return;
        }

        _isHovering = false;
        StopScrolling();
        InvalidateVisual();
    }

    /// <summary>
    /// Stops the frame timer when the control leaves the tree.
    /// </summary>
    /// <param name="e">Tree event.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopScrolling();
    }

    /// <summary>
    /// Restarts the scroll from the beginning whenever the title changes.
    /// </summary>
    /// <param name="change">Property change details.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty
            || change.Property == AutomaticallyScrollsProperty
            || change.Property == PointsPerSecondProperty)
        {
            StopScrolling();
        }
    }

    /// <summary>
    /// Lays out the title in the panel's title typeface.
    /// </summary>
    /// <param name="title">Text to lay out.</param>
    /// <returns>The shaped text, ready to measure or draw.</returns>
    private FormattedText CreateText(string title) => new(
        title,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        TitleTypeface,
        FontSize,
        Foreground ?? Brushes.Black);

    /// <summary>
    /// Computes how far the pair of titles has slid at this frame.
    /// </summary>
    /// <remarks>
    /// The cycle is a rest followed by a constant-speed slide of one
    /// <paramref name="distance"/>; it then repeats from zero, which is
    /// seamless because the second copy has by then reached the first
    /// copy's origin. The result is snapped to the device pixel grid.
    /// </remarks>
    /// <param name="distance">Title width plus gap.</param>
    /// <returns>A non-positive horizontal offset.</returns>
    private double ScrollOffset(double distance)
    {
        var speed = Math.Max(PointsPerSecond, 1);
        var pause = _isHovering && !AutomaticallyScrolls ? HoverInitialPause : InitialPause;
        var travel = TimeSpan.FromSeconds(distance / speed);
        var cycle = pause + travel;
        var elapsed = DateTimeOffset.UtcNow - _scrollStart;
        var phase = TimeSpan.FromTicks(elapsed.Ticks % cycle.Ticks);
        if (phase < pause)
        {
            return 0;
        }

        var offset = -(phase - pause).TotalSeconds * speed;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        return Math.Round(offset * scale) / scale;
    }

    /// <summary>
    /// Applies the trailing-edge fade for the rest of a drawing scope.
    /// </summary>
    /// <param name="context">Surface being drawn.</param>
    /// <param name="bounds">Area the fade covers.</param>
    /// <returns>A scope that removes the fade when disposed.</returns>
    private static DrawingContext.PushedState PushFade(DrawingContext context, Rect bounds)
    {
        var start = bounds.Width > 0 ? Math.Max(0, 1 - FadeWidth / bounds.Width) : 0;
        var mask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, start),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        return context.PushOpacityMask(mask, bounds.WithX(0).WithY(0));
    }

    /// <summary>
    /// Marks the start of a scroll cycle and begins issuing frames.
    /// </summary>
    private void StartScrolling()
    {
        _isScrolling = true;
        _scrollStart = DateTimeOffset.UtcNow;
        _frameTimer.Start();
    }

    /// <summary>
    /// Stops issuing frames and returns the title to its resting offset.
    /// </summary>
    private void StopScrolling()
    {
        if (!_isScrolling)
        {
            return;
        }

        _isScrolling = false;
        _frameTimer.Stop();
    }
}
