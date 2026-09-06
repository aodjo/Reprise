using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Scrolling synced lyrics beneath the card, one line per row.
/// </summary>
/// <remarks>
/// A port of the macOS <c>InlineLyricsView</c>. The line being sung sits
/// on the second row with the previous line above it and the next few
/// below, dimmed; the rows slide up as playback advances, with rows below
/// the current one starting a beat later so the move reads as a cascade,
/// and a vertical fade hides the rows entering and leaving. Everything is
/// drawn directly so the animation can run at frame rate without a
/// control per line.
/// </remarks>
public sealed class PanelLyricsView : Control
{
    /// <summary>
    /// Height of the view, matching the macOS lyrics viewport.
    /// </summary>
    public const double ViewportHeight = 136;

    /// <summary>
    /// Height of one lyric row.
    /// </summary>
    private const double RowHeight = 29;

    /// <summary>
    /// Space above the row that holds the previous line.
    /// </summary>
    private const double PreviousLineTopInset = 2;

    /// <summary>
    /// Horizontal padding either side of the text.
    /// </summary>
    private const double HorizontalPadding = 18;

    /// <summary>
    /// Furthest row below the current one that still gets a cascade delay.
    /// </summary>
    private const int MaximumCascadeStep = 3;

    /// <summary>
    /// Opacity of every row other than the current line.
    /// </summary>
    private const double DimmedOpacity = 0.38;

    /// <summary>
    /// Angular frequency of the slide, over its normalised duration.
    /// </summary>
    /// <remarks>
    /// SwiftUI's <c>spring(duration:bounce:)</c> defines the duration as one
    /// period of the underlying spring, so a full turn of the circle spans
    /// exactly <see cref="MoveDuration"/>. Getting this wrong is what makes
    /// a spring feel like a snap: at twice this frequency the row has
    /// travelled 96% of the way in a quarter of the time it was given.
    /// </remarks>
    private const double SpringOmega = Math.Tau;

    /// <summary>
    /// Damping ratio of the slide, the macOS bounce of 0.24 subtracted from one.
    /// </summary>
    private const double SpringDamping = 0.76;

    private static readonly TimeSpan CascadeDelay = TimeSpan.FromMilliseconds(45);
    private static readonly TimeSpan MoveDuration = TimeSpan.FromMilliseconds(560);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(220);
    private static readonly Typeface LineTypeface = new(PanelTypography.Family, FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>
    /// Brush the lines are drawn with.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<PanelLyricsView, IBrush?>(nameof(Foreground), Brushes.Black);

    /// <summary>
    /// Brush of the hairline along the top edge.
    /// </summary>
    public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
        AvaloniaProperty.Register<PanelLyricsView, IBrush?>(nameof(SeparatorBrush));

    private readonly DispatcherTimer _frameTimer;
    private readonly Dictionary<int, FormattedText> _textCache = [];
    private SyncedLyrics? _lyrics;
    private int _focus;
    private double _previousFocus;
    private DateTimeOffset _moveStartedAt;
    private bool _moving;
    private int? _current;
    private int? _previousCurrent;
    private DateTimeOffset _fadeStartedAt;
    private bool _fading;
    private double _cachedWidth;

    /// <summary>
    /// Registers the property watchers that trigger a redraw.
    /// </summary>
    static PanelLyricsView()
    {
        AffectsRender<PanelLyricsView>(ForegroundProperty, SeparatorBrushProperty);
    }

    /// <summary>
    /// Creates the view with its frame timer stopped.
    /// </summary>
    public PanelLyricsView()
    {
        _frameTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            (_, _) => Tick());
        _frameTimer.Stop();
        ClipToBounds = true;
        Height = ViewportHeight;
    }

    /// <summary>
    /// Brush the lines are drawn with.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Brush of the hairline along the top edge.
    /// </summary>
    public IBrush? SeparatorBrush
    {
        get => GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    /// <summary>
    /// Lyrics to display, or null for none.
    /// </summary>
    /// <remarks>
    /// Assigning new lyrics resets the view without animation, since the
    /// rows of one song have no relation to the rows of the next.
    /// </remarks>
    public SyncedLyrics? Lyrics
    {
        get => _lyrics;
        set
        {
            if (ReferenceEquals(_lyrics, value))
            {
                return;
            }

            _lyrics = value;
            _textCache.Clear();
            _focus = 0;
            _previousFocus = 0;
            _current = null;
            _previousCurrent = null;
            StopAnimation();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Moves the view to the line being sung at a position.
    /// </summary>
    /// <remarks>
    /// Called by the panel's progress timer. A change of line starts the
    /// slide; an unchanged line costs nothing.
    /// <para>
    /// Two indices are tracked, and the distinction matters. The focused
    /// line is what the rows are positioned around and always exists once
    /// playback has begun; the current line is the one actually being sung,
    /// and is absent through an instrumental gap. So the sheet holds its
    /// place while the highlight fades away, rather than jumping whenever
    /// a line runs out.
    /// </para>
    /// </remarks>
    /// <param name="position">Current playback position.</param>
    /// <example>
    /// <code>
    /// lyricsView.Advance(viewModel.DisplayedPosition());
    /// </code>
    /// </example>
    public void Advance(TimeSpan position)
    {
        if (_lyrics is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var focus = _lyrics.FocusedLineIndex(position) ?? 0;
        var current = _lyrics.LineIndex(position);

        if (focus != _focus)
        {
            _previousFocus = _moving ? AnimatedFocus(now, 0) : _focus;
            _focus = focus;
            _moveStartedAt = now;
            _moving = true;
        }

        if (current != _current)
        {
            _previousCurrent = _current;
            _current = current;
            _fadeStartedAt = now;
            _fading = true;
        }

        if (!_moving && !_fading)
        {
            return;
        }

        _frameTimer.Start();
        InvalidateVisual();
    }

    /// <summary>
    /// Draws the hairline, then every row that falls inside the viewport.
    /// </summary>
    /// <param name="context">Surface to draw on.</param>
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (SeparatorBrush is { } separator)
        {
            context.DrawRectangle(separator, null, new Rect(0, 0, bounds.Width, 1));
        }

        if (_lyrics is not { } lyrics || lyrics.Lines.Count == 0 || bounds.Width <= 0)
        {
            return;
        }

        if (Math.Abs(bounds.Width - _cachedWidth) > 0.5)
        {
            _cachedWidth = bounds.Width;
            _textCache.Clear();
        }

        var now = DateTimeOffset.UtcNow;
        using var mask = context.PushOpacityMask(CreateFadeMask(), bounds);
        // Span both ends of a slide, or the rows the sheet is travelling
        // from would pop into place instead of moving out of view.
        var lowest = (int)Math.Floor(Math.Min(_previousFocus, _focus));
        var highest = (int)Math.Ceiling(Math.Max(_previousFocus, _focus));
        var firstVisible = Math.Max(0, lowest - 3);
        var lastVisible = Math.Min(lyrics.Lines.Count - 1, highest + 6);
        for (var index = firstVisible; index <= lastVisible; index++)
        {
            var relative = index - _focus;
            var cascade = Math.Clamp(relative, 0, MaximumCascadeStep);
            var y = PreviousLineTopInset + (index - AnimatedFocus(now, cascade) + 1) * RowHeight;
            if (y + RowHeight < 0 || y > bounds.Height)
            {
                continue;
            }

            var opacity = AnimatedOpacity(now, index);
            var text = TextFor(index, lyrics.Lines[index].Text, bounds.Width);
            using var pushed = context.PushOpacity(opacity);
            context.DrawText(text, new Point(HorizontalPadding, y + (RowHeight - text.Height) / 2));
        }
    }

    /// <summary>
    /// Takes the full width offered and the fixed viewport height.
    /// </summary>
    /// <param name="availableSize">Space offered by the parent.</param>
    /// <returns>The viewport size.</returns>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, ViewportHeight);

    /// <summary>
    /// Stops the frame timer when the view leaves the tree.
    /// </summary>
    /// <param name="e">Tree event.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopAnimation();
    }

    /// <summary>
    /// Redraws the slide and stops the timer once every row has settled.
    /// </summary>
    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        if (_moving && now - _moveStartedAt >= MoveDuration + CascadeDelay * MaximumCascadeStep)
        {
            _moving = false;
            _previousFocus = _focus;
        }

        if (_fading && now - _fadeStartedAt >= FadeDuration)
        {
            _fading = false;
            _previousCurrent = _current;
        }

        if (!_moving && !_fading)
        {
            _frameTimer.Stop();
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Ends both animations and snaps every row to its final state.
    /// </summary>
    private void StopAnimation()
    {
        _moving = false;
        _fading = false;
        _previousFocus = _focus;
        _previousCurrent = _current;
        _frameTimer.Stop();
    }

    /// <summary>
    /// The row index the view is centred on at a moment, mid-slide.
    /// </summary>
    /// <param name="now">Moment to evaluate at.</param>
    /// <param name="cascadeStep">Delay steps for the row being placed.</param>
    /// <returns>A fractional row index between the previous and current focus.</returns>
    private double AnimatedFocus(DateTimeOffset now, int cascadeStep)
    {
        if (!_moving)
        {
            return _focus;
        }

        var elapsed = now - _moveStartedAt - CascadeDelay * cascadeStep;
        var progress = Math.Clamp(elapsed.TotalMilliseconds / MoveDuration.TotalMilliseconds, 0, 1);
        return _previousFocus + (_focus - _previousFocus) * Spring(progress);
    }

    /// <summary>
    /// Opacity of a row, fading between dimmed and full as focus moves.
    /// </summary>
    /// <param name="now">Moment to evaluate at.</param>
    /// <param name="index">Row to evaluate.</param>
    /// <returns>An opacity from <see cref="DimmedOpacity"/> to 1.</returns>
    private double AnimatedOpacity(DateTimeOffset now, int index)
    {
        var target = index == _current ? 1 : DimmedOpacity;
        if (!_fading)
        {
            return target;
        }

        var previous = index == _previousCurrent ? 1 : DimmedOpacity;
        var progress = Math.Clamp((now - _fadeStartedAt).TotalMilliseconds / FadeDuration.TotalMilliseconds, 0, 1);
        return previous + (target - previous) * EaseInOut(progress);
    }

    /// <summary>
    /// A lightly under-damped spring, standing in for SwiftUI's bouncy spring.
    /// </summary>
    /// <param name="progress">Normalised time from 0 to 1.</param>
    /// <returns>Position from 0 to about 1, with a small overshoot.</returns>
    internal static double Spring(double progress)
    {
        if (progress >= 1)
        {
            return 1;
        }

        var damped = SpringOmega * Math.Sqrt(1 - SpringDamping * SpringDamping);
        return 1 - Math.Exp(-SpringDamping * SpringOmega * progress)
            * (Math.Cos(damped * progress) + SpringDamping * SpringOmega / damped * Math.Sin(damped * progress));
    }

    /// <summary>
    /// The ease the highlight crossfades on.
    /// </summary>
    /// <remarks>
    /// Stands in for SwiftUI's <c>easeInOut</c>. A straight ramp makes the
    /// handover between two lines read as a dip in brightness, because both
    /// sit at middling opacity for the whole of the middle of the fade.
    /// </remarks>
    /// <param name="progress">Normalised time from 0 to 1.</param>
    /// <returns>Eased position from 0 to 1.</returns>
    internal static double EaseInOut(double progress) =>
        progress * progress * (3 - 2 * progress);

    /// <summary>
    /// The mask that fades rows at the top and bottom edges.
    /// </summary>
    /// <returns>A vertical gradient, opaque through the middle.</returns>
    private static LinearGradientBrush CreateFadeMask() => new()
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Colors.Transparent, 0),
            new GradientStop(Colors.Black, 0.18),
            new GradientStop(Colors.Black, 0.82),
            new GradientStop(Colors.Transparent, 1),
        },
    };

    /// <summary>
    /// Lays out one line, caching the result per row.
    /// </summary>
    /// <param name="index">Row the text belongs to.</param>
    /// <param name="text">Words to lay out.</param>
    /// <param name="width">Current view width.</param>
    /// <returns>Shaped text centred in the row, trimmed to one line.</returns>
    private FormattedText TextFor(int index, string text, double width)
    {
        if (_textCache.TryGetValue(index, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LineTypeface,
            PanelTypography.Lyric,
            Foreground ?? Brushes.Black)
        {
            MaxTextWidth = Math.Max(1, width - HorizontalPadding * 2),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        _textCache[index] = formatted;
        return formatted;
    }

    /// <summary>
    /// Drops cached text when the brush changes.
    /// </summary>
    /// <param name="change">Property change details.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty)
        {
            _textCache.Clear();
        }
    }
}
