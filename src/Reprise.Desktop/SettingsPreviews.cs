using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>
/// The sample album cover the settings previews use, matching the macOS
/// <c>PreviewAlbumArtwork</c>: a conic rainbow with a music note.
/// </summary>
public sealed class PreviewArtwork : Border
{
    /// <summary>
    /// Creates the cover at a size.
    /// </summary>
    /// <param name="size">Edge length.</param>
    /// <param name="cornerRadius">Corner rounding.</param>
    /// <param name="symbolSize">Size of the note glyph.</param>
    public PreviewArtwork(double size, double cornerRadius, double symbolSize)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(cornerRadius);
        ClipToBounds = true;
        Background = new ConicGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xF0, 0x45, 0x59), 0),
                new GradientStop(Color.FromRgb(0xFA, 0xAD, 0x38), 0.25),
                new GradientStop(Color.FromRgb(0x38, 0xB8, 0xC7), 0.5),
                new GradientStop(Color.FromRgb(0x6B, 0x40, 0xB8), 0.75),
                new GradientStop(Color.FromRgb(0xF0, 0x45, 0x59), 1),
            },
        };
        Child = new PanelGlyph
        {
            Icon = PanelIcons.MusicNote,
            IconSize = symbolSize,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}

/// <summary>
/// A live mock of the player panel, used by the theme tab.
/// </summary>
/// <remarks>
/// Rebuilt from the same measurements as <see cref="NowPlayingWindow"/> -
/// 360 by 140, a 112-point cover, the transport row - so the preview shows
/// the actual panel colours rather than an approximation.
/// </remarks>
public sealed class ThemePanelPreview : Border
{
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly PanelGlyph _logo;
    private readonly PanelGlyph[] _controls;
    private readonly CompactSlider _slider;
    private readonly TextBlock _leading;
    private readonly TextBlock _trailing;
    private const double PreviewDuration = 193;

    /// <summary>
    /// Builds the mock with placeholder track data.
    /// </summary>
    public ThemePanelPreview()
    {
        Width = 360;
        Height = 140;
        CornerRadius = new CornerRadius(16);
        ClipToBounds = true;
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x6B, 0, 0, 0));
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, OffsetY = 2, Color = Color.FromArgb(0x29, 0, 0, 0) });
        HorizontalAlignment = HorizontalAlignment.Center;

        _title = new TextBlock { Text = "음악 제목 미리보기", FontSize = 13, FontWeight = FontWeight.SemiBold };
        _artist = new TextBlock { Text = "아티스트", FontSize = 11 };
        _logo = new PanelGlyph { Icon = PanelIcons.MusicNote, IconSize = 17, Width = 19, Height = 19, VerticalAlignment = VerticalAlignment.Top };
        _controls =
        [
            new PanelGlyph { Icon = PanelIcons.Backward, IconSize = 18, Width = 36, Height = 34 },
            new PanelGlyph { Icon = PanelIcons.Pause, IconSize = 24, Width = 36, Height = 34 },
            new PanelGlyph { Icon = PanelIcons.Forward, IconSize = 18, Width = 36, Height = 34 },
        ];
        _slider = new CompactSlider { Minimum = 0, Maximum = PreviewDuration, Value = 69 };
        _slider.UserValueChanged += (_, _) => UpdateTimes();
        _leading = new TextBlock { FontSize = 10 };
        _trailing = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titles = new StackPanel { Spacing = 2, Children = { _title, _artist } };
        header.Children.Add(titles);
        header.Children.Add(_logo);
        Grid.SetColumn(_logo, 1);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var control in _controls)
        {
            controls.Children.Add(control);
        }

        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _leading, _trailing } };
        Grid.SetColumn(_trailing, 1);
        var progress = new StackPanel { Spacing = 1, Children = { _slider, times } };

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(14, 0, 0, 0),
            Height = 112,
            Children = { header, controls, progress },
        };
        Grid.SetRow(controls, 1);
        Grid.SetRow(progress, 2);

        var card = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(14),
            Children = { new PreviewArtwork(112, 11, 28), column },
        };
        Grid.SetColumn(column, 1);
        Child = card;
    }

    /// <summary>
    /// Repaints the mock for a theme and time settings.
    /// </summary>
    /// <param name="theme">Theme to preview.</param>
    /// <param name="preferences">Time label settings.</param>
    /// <param name="systemIsDark">Whether the desktop is in dark mode.</param>
    /// <param name="accent">Desktop accent colour.</param>
    public void Update(PanelTheme theme, DesktopPreferences preferences, bool systemIsDark, Color accent)
    {
        var palette = PanelPalette.Resolve(theme, systemIsDark, accent, PanelTranslucency.Transparent);
        Background = new SolidColorBrush(palette.Background);
        var primary = new SolidColorBrush(palette.Primary);
        var secondary = new SolidColorBrush(palette.Secondary);
        _title.Foreground = primary;
        _artist.Foreground = secondary;
        _logo.Foreground = primary;
        foreach (var control in _controls)
        {
            control.Foreground = new SolidColorBrush(palette.Control);
        }

        _slider.TrackBrush = new SolidColorBrush(palette.Track);
        _slider.FillBrush = new SolidColorBrush(palette.Accent);
        _leading.Foreground = secondary;
        _trailing.Foreground = secondary;
        _preferences = preferences;
        UpdateTimes();
    }

    private DesktopPreferences _preferences = new();

    /// <summary>
    /// Rewrites the time labels from the slider position.
    /// </summary>
    private void UpdateTimes()
    {
        var position = TimeSpan.FromSeconds(_slider.Value);
        var duration = TimeSpan.FromSeconds(PreviewDuration);
        _leading.Text = PanelTimeDisplay.LeadingText(_preferences.LeadingTimeStyle, position);
        _trailing.Text = PanelTimeDisplay.TrailingText(_preferences.TrailingTimeStyle, duration, duration - position);
    }
}

/// <summary>
/// A mock of the tray entry on a blue top bar, used by the menu bar tab.
/// </summary>
public sealed class MenuBarPreview : Border
{
    private readonly PreviewArtwork _artwork;
    private readonly PanelTitleMarquee _marquee;

    /// <summary>
    /// Builds the mock.
    /// </summary>
    public MenuBarPreview()
    {
        Height = 30;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(10, 0);
        HorizontalAlignment = HorizontalAlignment.Center;
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x14, 0x6B, 0xA6), 0),
                new GradientStop(Color.FromRgb(0x08, 0x4D, 0x85), 1),
            },
        };
        _artwork = new PreviewArtwork(18, 4, 8) { VerticalAlignment = VerticalAlignment.Center };
        _marquee = new PanelTitleMarquee { Height = 30, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children = { _artwork, _marquee },
        };
    }

    /// <summary>
    /// Repaints the mock for the current tray settings.
    /// </summary>
    /// <param name="preferences">Tray settings.</param>
    public void Update(DesktopPreferences preferences)
    {
        _artwork.IsVisible = preferences.MenuBarArtworkStyle != MenuBarArtworkStyle.Hidden;
        var hidden = preferences.MenuBarTitleFormat == MenuBarTitleFormat.Hidden;
        _marquee.IsVisible = !hidden;
        var text = preferences.MenuBarShowsLyrics && !hidden
            ? "다시 만나요"
            : MenuBarText.FormatTitle(preferences.MenuBarTitleFormat, "여기에 재생 중인 음악 제목이 표시됩니다", "미리보기 아티스트");
        _marquee.Title = text;
        _marquee.AutomaticallyScrolls = preferences.AutomaticallyScrollsTitles;
        _marquee.PointsPerSecond = preferences.MarqueePointsPerSecond;
        var maximum = preferences.MenuBarLabelLength * 7.5;
        var natural = Math.Min(text.Length * 7.5, 220);
        _marquee.Width = preferences.MenuBarShowsLyrics && preferences.MenuBarReservesLabelWidth
            ? maximum
            : Math.Min(natural, maximum);
    }
}

/// <summary>
/// A mock of the scrubber and its time labels, used by the panel tab.
/// </summary>
public sealed class PanelTimePreview : Border
{
    private const double PreviewDuration = 193;
    private readonly CompactSlider _slider;
    private readonly TextBlock _leading;
    private readonly TextBlock _trailing;
    private DesktopPreferences _preferences = new();

    /// <summary>
    /// Builds the mock.
    /// </summary>
    public PanelTimePreview()
    {
        Width = 300;
        Padding = new Thickness(12);
        CornerRadius = new CornerRadius(10);
        HorizontalAlignment = HorizontalAlignment.Center;
        this.Bind(BackgroundProperty, this.GetResourceObservable("SystemControlBackgroundBaseLowBrush"));
        _slider = new CompactSlider { Minimum = 0, Maximum = PreviewDuration, Value = 69 };
        _slider.UserValueChanged += (_, _) => UpdateTimes();
        _slider.Bind(CompactSlider.TrackBrushProperty, _slider.GetResourceObservable("SystemControlBackgroundBaseMediumLowBrush"));
        _slider.Bind(CompactSlider.FillBrushProperty, _slider.GetResourceObservable("SystemAccentColorBrush"));
        _leading = new TextBlock { FontSize = 10 };
        _trailing = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
        _leading.Bind(TextBlock.ForegroundProperty, _leading.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
        _trailing.Bind(TextBlock.ForegroundProperty, _trailing.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _leading, _trailing } };
        Grid.SetColumn(_trailing, 1);
        Child = new StackPanel { Spacing = 1, Children = { _slider, times } };
        UpdateTimes();
    }

    /// <summary>
    /// Repaints the labels for the chosen styles.
    /// </summary>
    /// <param name="preferences">Time label settings.</param>
    public void Update(DesktopPreferences preferences)
    {
        _preferences = preferences;
        UpdateTimes();
    }

    /// <summary>
    /// Rewrites the time labels from the slider position.
    /// </summary>
    private void UpdateTimes()
    {
        var position = TimeSpan.FromSeconds(_slider.Value);
        var duration = TimeSpan.FromSeconds(PreviewDuration);
        _leading.Text = PanelTimeDisplay.LeadingText(_preferences.LeadingTimeStyle, position);
        _trailing.Text = PanelTimeDisplay.TrailingText(_preferences.TrailingTimeStyle, duration, duration - position);
    }
}
