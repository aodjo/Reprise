using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// The player panel: artwork, title, transport, scrubber, and footer.
/// </summary>
/// <remarks>
/// A port of the macOS <c>PlayerPopoverView</c> to Avalonia, laid out to the
/// same proportions: a 360-wide borderless panel with 16-point corners, a
/// card whose cover matches its text column, a 136-point lyrics strip when
/// synced lyrics exist, and a 30-point footer. It behaves like a popover too -
/// it hides when it loses focus or on Escape, and the tray icon brings it
/// back - rather than like a document window.
/// <para>
/// Built in code rather than XAML so the desktop assembly stays a plain
/// library with no compiled markup, which keeps the platform heads that
/// reference it free of Avalonia's XAML build steps. Controls are updated
/// by pushing values from <see cref="UpdateView"/> rather than through
/// bindings; at this size that is less machinery and easier to follow.
/// </para>
/// </remarks>
public sealed class NowPlayingWindow : Window
{
    /// <summary>
    /// Width of the panel, matching <c>PlayerPanelLayout.defaultSize</c>.
    /// </summary>
    public const double PanelWidth = 360;

    /// <summary>
    /// Corner radius of the panel, matching <c>PlayerPanelLayout.cornerRadius</c>.
    /// </summary>
    public const double PanelCornerRadius = 16;

    /// <summary>
    /// Smallest the album cover is allowed to be.
    /// </summary>
    /// <remarks>
    /// The cover is square and as tall as the text column beside it, so its
    /// size follows the type scale rather than a fixed number. This floor
    /// only matters before the column has been measured.
    /// </remarks>
    private const double MinimumArtworkSize = 72;

    /// <summary>
    /// Gap kept between the panel and the edge of the screen when first shown.
    /// </summary>
    private const int ScreenMargin = 6;

    private static readonly Color WarningColor = Color.FromRgb(0xFF, 0x95, 0x00);

    /// <summary>
    /// How long after being shown the panel may take its focus back.
    /// </summary>
    /// <remarks>
    /// Clicking a tray icon hands focus to the desktop shell, and some
    /// shells keep it: the panel appears, loses focus in the same instant,
    /// and hides itself again, so the icon looks like it needs a second
    /// click. Within this window the panel asks for focus once instead of
    /// hiding; after it, a focus loss dismisses the panel as usual.
    /// </remarks>
    private static readonly TimeSpan FocusGracePeriod = TimeSpan.FromMilliseconds(600);

    private readonly NowPlayingViewModel _viewModel;
    private readonly PreferencesStore _preferences;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _progressTimer;
    private readonly Border _root;
    private readonly ContentControl _body;
    private readonly Grid _card;
    private readonly Border _artworkFrame;
    private readonly Image _artworkImage;
    private readonly Border _artworkPlaceholder;
    private readonly PanelGlyph _artworkGlyph;
    private readonly PanelTitleMarquee _title;
    private readonly TextBlock _artist;
    private readonly PanelGlyph _playerLogo;
    private readonly PanelIconButton _previousButton;
    private readonly PanelIconButton _playPauseButton;
    private readonly PanelIconButton _nextButton;
    private readonly CompactSlider _seekSlider;
    private readonly TextBlock _leadingTime;
    private readonly TextBlock _trailingTime;
    private readonly StackPanel _empty;
    private readonly PanelGlyph _emptyIcon;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptyDescription;
    private readonly PanelLyricsView _lyricsView;
    private readonly Border _errorBanner;
    private readonly PanelGlyph _errorIcon;
    private readonly TextBlock _errorText;
    private readonly Border _footerRule;
    private readonly Grid _footer;
    private readonly TextBlock _versionText;
    private readonly PanelIconButton _volumeButton;
    private readonly PanelIconButton _settingsButton;
    private readonly PanelIconButton _exitButton;
    private readonly Popup _volumePopup;
    private readonly Border _volumePanel;
    private readonly PanelIconButton _muteButton;
    private readonly CompactSlider _volumeSlider;
    private readonly TextBlock _volumeValue;
    private PanelPalette _palette;
    private Bitmap? _artworkBitmap;
    private byte[]? _artworkBytes;
    private bool _wasActivated;
    private bool _positioned;
    private DateTimeOffset _shownAt;
    private bool _reclaimedFocus;

    /// <summary>
    /// Whether a close request should actually close the window.
    /// </summary>
    /// <remarks>
    /// False during normal use, so a close request hides the panel to the
    /// tray instead. Quitting sets this to true first, which is the only way
    /// the window is allowed to close for real.
    /// </remarks>
    public bool AllowClose { get; set; }

    /// <summary>
    /// Raised when the user presses the footer's exit button.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Raised when the user presses the footer's gear button.
    /// </summary>
    public event EventHandler? SettingsRequested;


    /// <summary>
    /// Builds the panel and starts polling for playback state.
    /// </summary>
    /// <remarks>
    /// Takes ownership of the view model: the window disposes it when it
    /// finally closes. Preferences are observed for the lifetime of the
    /// window so a theme change from the settings menu repaints at once.
    /// </remarks>
    /// <param name="viewModel">State to display and command.</param>
    /// <param name="preferences">Panel settings to honour and edit.</param>
    /// <example>
    /// <code>
    /// desktop.MainWindow = new NowPlayingWindow(
    ///     new NowPlayingViewModel(service),
    ///     new PreferencesStore(PreferencesStore.DefaultPath));
    /// </code>
    /// </example>
    public NowPlayingWindow(NowPlayingViewModel viewModel, PreferencesStore preferences)
    {
        _viewModel = viewModel;
        _preferences = preferences;
        _palette = PanelPalette.Resolve(
            preferences.Current.PanelTheme,
            systemIsDark: true,
            Colors.DodgerBlue,
            PanelTranslucency.None);

        Title = "Reprise";
        Width = PanelWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.None,
        ];
        Background = Brushes.Transparent;

        _artworkImage = new Image { Stretch = Stretch.UniformToFill };
        _artworkGlyph = new PanelGlyph
        {
            IconSize = Math.Max(MinimumArtworkSize * 0.32, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _artworkPlaceholder = new Border { Child = _artworkGlyph };
        _artworkFrame = new Border
        {
            Width = MinimumArtworkSize,
            Height = MinimumArtworkSize,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Panel { Children = { _artworkPlaceholder, _artworkImage } },
        };

        _title = new PanelTitleMarquee { Height = 16, FontSize = PanelTypography.Title };
        _artist = new TextBlock
        {
            FontSize = PanelTypography.Subtitle,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _playerLogo = new PanelGlyph
        {
            Width = 21,
            Height = 21,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };

        _previousButton = CreateControlButton(PanelIcons.Backward, 19, "이전 곡");
        _playPauseButton = CreateControlButton(PanelIcons.Play, 25, "재생");
        _nextButton = CreateControlButton(PanelIcons.Forward, 19, "다음 곡");
        _previousButton.Click += async (_, _) => await _viewModel.SendAsync(PlaybackCommand.Previous);
        _playPauseButton.Click += async (_, _) => await _viewModel.SendAsync(PlaybackCommand.PlayPause);
        _nextButton.Click += async (_, _) => await _viewModel.SendAsync(PlaybackCommand.Next);

        _seekSlider = new CompactSlider { Minimum = 0, Maximum = 1 };
        _seekSlider.DragStarted += (_, _) => _viewModel.BeginSeek(TimeSpan.FromSeconds(_seekSlider.Value));
        _seekSlider.UserValueChanged += (_, value) => _viewModel.UpdateSeek(TimeSpan.FromSeconds(value));
        _seekSlider.DragCompleted += async (_, _) => await _viewModel.EndSeekAsync();
        ToolTip.SetTip(_seekSlider, "재생 위치 이동");
        _leadingTime = new TextBlock { FontSize = PanelTypography.Caption };
        _trailingTime = new TextBlock { FontSize = PanelTypography.Caption, HorizontalAlignment = HorizontalAlignment.Right };

        _card = BuildCard();

        _emptyIcon = new PanelGlyph
        {
            IconSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _emptyTitle = new TextBlock
        {
            FontSize = PanelTypography.Heading,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _emptyDescription = new TextBlock
        {
            FontSize = PanelTypography.Subtitle,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _empty = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(16),
            MinHeight = 150,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _emptyIcon, _emptyTitle, _emptyDescription },
        };

        _body = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _lyricsView = new PanelLyricsView { IsVisible = false };

        _errorIcon = new PanelGlyph
        {
            Icon = PanelIcons.Warning,
            IconSize = 13,
            Foreground = new SolidColorBrush(WarningColor),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
        };
        _errorText = new TextBlock
        {
            FontSize = PanelTypography.Caption,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _errorBanner = new Border
        {
            Margin = new Thickness(14, 0, 14, 14),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(PanelPalette.WithAlpha(WarningColor, 0.12)),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children = { _errorIcon, _errorText },
            },
            IsVisible = false,
        };
        Grid.SetColumn(_errorText, 1);

        _versionText = new TextBlock
        {
            FontSize = PanelTypography.Caption,
            Text = VersionText(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _volumeButton = CreateFooterButton(PanelIcons.SpeakerWave3, "음량");
        _settingsButton = CreateFooterButton(PanelIcons.Gear, "설정 열기");
        _exitButton = CreateFooterButton(PanelIcons.Exit, "Reprise 종료");
        _volumeButton.Click += (_, _) => ToggleVolumePopup();
        _settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _footerRule = new Border { Height = 1 };
        _footer = new Grid
        {
            Height = 30,
            Margin = new Thickness(14, 0),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { _versionText, _volumeButton, _settingsButton, _exitButton },
        };
        Grid.SetColumn(_volumeButton, 1);
        Grid.SetColumn(_settingsButton, 2);
        Grid.SetColumn(_exitButton, 3);

        _muteButton = new PanelIconButton
        {
            Width = 18,
            Height = 22,
            IconSize = 11,
            Icon = PanelIcons.SpeakerWave3,
        };
        _muteButton.Click += async (_, _) => await _viewModel.ToggleMuteAsync();
        _volumeSlider = new CompactSlider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
        _volumeSlider.DragStarted += (_, _) => _viewModel.IsVolumeEditing = true;
        _volumeSlider.UserValueChanged += async (_, value) =>
            await _viewModel.SetVolumeAsync((int)Math.Round(value));
        _volumeSlider.DragCompleted += (_, _) => _viewModel.IsVolumeEditing = false;
        _volumeValue = new TextBlock
        {
            FontSize = PanelTypography.Caption,
            Width = 23,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var volumeRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Children = { _muteButton, _volumeSlider, _volumeValue },
        };
        Grid.SetColumn(_volumeSlider, 1);
        Grid.SetColumn(_volumeValue, 2);
        _volumePanel = new Border
        {
            Width = 160,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0),
            Child = volumeRow,
        };
        _volumePopup = new Popup
        {
            Child = _volumePanel,
            PlacementTarget = _footer,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopRight,
            PlacementGravity = PopupGravity.TopLeft,
            VerticalOffset = -6,
            IsLightDismissEnabled = true,
        };
        _volumePopup.Closed += (_, _) => _viewModel.IsVolumeEditing = false;

        _root = new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius),
            ClipToBounds = true,
            Child = new Panel
            {
                Children =
                {
                    new StackPanel
                    {
                        Children = { _body, _lyricsView, _errorBanner, _footerRule, _footer },
                    },
                    _volumePopup,
                },
            },
        };
        _root.PointerPressed += HandleRootPointerPressed;
        Content = _root;

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            async (_, _) => await _viewModel.RefreshAsync());
        _progressTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => UpdateProgress());

        _viewModel.PropertyChanged += HandleViewModelChanged;
        _preferences.Changed += HandlePreferencesChanged;
        Opened += HandleOpened;
        Activated += (_, _) => _wasActivated = true;
        Deactivated += HandleDeactivated;
        KeyDown += HandleKeyDown;
        Closing += HandleClosing;
        Closed += HandleClosed;

        ApplyPalette();
        UpdateView();
        _refreshTimer.Start();
        _progressTimer.Start();
    }

    /// <summary>
    /// Shows the panel, placing it by the tray corner the first time.
    /// </summary>
    /// <remarks>
    /// Linux offers no way to learn where the tray icon is, so the first
    /// placement is the top-right of the primary screen, where most desktops
    /// keep their status area. The user can drag the panel anywhere and it
    /// keeps that position for later shows.
    /// </remarks>
    /// <example>
    /// <code>
    /// trayIcon.Clicked += (_, _) => window.ShowPanel();
    /// </code>
    /// </example>
    public void ShowPanel()
    {
        if (!_positioned)
        {
            PositionByTray();
        }

        _shownAt = DateTimeOffset.UtcNow;
        _reclaimedFocus = false;
        Show();
        Activate();
        if (_preferences.Current.ResetsMenuTitleWhenPanelOpens)
        {
            _title.RestartScroll();
        }

        _ = _viewModel.RefreshAsync();
    }

    /// <summary>
    /// Hides the panel to the tray, closing any pop-ups it owns.
    /// </summary>
    public void HidePanel()
    {
        _volumePopup.IsOpen = false;
        _wasActivated = false;
        Hide();
    }

    /// <summary>
    /// Shows the panel if hidden, hides it if visible.
    /// </summary>
    /// <example>
    /// <code>
    /// trayIcon.Clicked += (_, _) => window.TogglePanel();
    /// </code>
    /// </example>
    public void TogglePanel()
    {
        if (IsVisible)
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }
    }

    /// <summary>
    /// Repaints when the window's transparency is finally known.
    /// </summary>
    /// <remarks>
    /// The windowing system reports what it granted only after the window
    /// exists, and the Liquid theme's opacity depends on the answer.
    /// </remarks>
    /// <param name="change">Property change details.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty)
        {
            ApplyPalette();
        }
    }

    /// <summary>
    /// Assembles the card shown while a track is loaded.
    /// </summary>
    /// <remarks>
    /// Artwork on the left, then a column of title and artist with the
    /// player mark at its top-right, the transport centred, and the scrubber
    /// with its two time labels at the bottom - the macOS card exactly.
    /// <para>
    /// The column sizes itself to its content and the cover follows, rather
    /// than both being pinned to one number: the two then stay exactly as
    /// tall as each other whatever the type scale, and the card has no slack
    /// to spread between its rows.
    /// </para>
    /// </remarks>
    /// <returns>The card's content tree.</returns>
    private Grid BuildCard()
    {
        var titles = new StackPanel
        {
            Spacing = 2,
            Children = { _title, _artist },
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { titles, _playerLogo },
        };
        Grid.SetColumn(_playerLogo, 1);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _previousButton, _playPauseButton, _nextButton },
        };

        var times = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _leadingTime, _trailingTime },
        };
        Grid.SetColumn(_trailingTime, 1);
        var progress = new StackPanel
        {
            Spacing = 1,
            Children = { _seekSlider, times },
        };

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 6,
            Margin = new Thickness(12, 0, 0, 0),
            Children = { header, controls, progress },
        };
        Grid.SetRow(controls, 1);
        Grid.SetRow(progress, 2);
        column.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                ResizeArtwork(column.Bounds.Height);
            }
        };

        var card = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(14),
            Children = { _artworkFrame, column },
        };
        Grid.SetColumn(column, 1);
        return card;
    }

    /// <summary>
    /// Squares the album cover off against the height of the text column.
    /// </summary>
    /// <param name="height">Measured height of the column beside it.</param>
    private void ResizeArtwork(double height)
    {
        var size = Math.Round(Math.Max(height, MinimumArtworkSize));
        if (Math.Abs(_artworkFrame.Width - size) < 0.5)
        {
            return;
        }

        _artworkFrame.Width = size;
        _artworkFrame.Height = size;
        _artworkFrame.CornerRadius = new CornerRadius(Math.Round(size * 0.1));
        _artworkGlyph.IconSize = Math.Max(size * 0.32, 10);
    }

    /// <summary>
    /// Creates one of the three transport buttons.
    /// </summary>
    /// <param name="icon">Glyph to draw.</param>
    /// <param name="iconSize">Glyph size; larger for play/pause.</param>
    /// <param name="label">Tooltip text.</param>
    /// <returns>A 36 by 34 chrome-less button.</returns>
    private static PanelIconButton CreateControlButton(Geometry icon, double iconSize, string label)
    {
        var button = new PanelIconButton
        {
            Width = 36,
            Height = 34,
            Icon = icon,
            IconSize = iconSize,
        };
        ToolTip.SetTip(button, label);
        return button;
    }

    /// <summary>
    /// Creates one of the footer's small buttons.
    /// </summary>
    /// <param name="icon">Glyph to draw.</param>
    /// <param name="label">Tooltip text.</param>
    /// <returns>A 19 by 19 chrome-less button.</returns>
    private static PanelIconButton CreateFooterButton(Geometry icon, string label)
    {
        var button = new PanelIconButton
        {
            Width = 19,
            Height = 19,
            Icon = icon,
            IconSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, label);
        return button;
    }

    /// <summary>
    /// Formats the footer's version line.
    /// </summary>
    /// <remarks>
    /// Reads the informational version so prerelease suffixes survive, and
    /// drops the build metadata the SDK appends after a plus sign.
    /// </remarks>
    /// <returns>Text such as <c>Reprise v2.0.0-alpha.1</c>.</returns>
    private static string VersionText()
    {
        var informational = typeof(NowPlayingWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational)
            ? typeof(NowPlayingWindow).Assembly.GetName().Version?.ToString(3) ?? "-"
            : informational.Split('+', 2)[0];
        return $"Reprise v{version}";
    }

    /// <summary>
    /// Places the panel at the top-right of the primary screen.
    /// </summary>
    private void PositionByTray()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)Math.Round(PanelWidth * scale);
        var margin = (int)Math.Round(ScreenMargin * scale);
        Position = new PixelPoint(
            Math.Max(area.X, area.Right - width - margin),
            area.Y + margin);
        _positioned = true;
    }

    /// <summary>
    /// Subscribes to desktop colour changes once the platform is reachable.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">Unused.</param>
    private void HandleOpened(object? sender, EventArgs e)
    {
        if (Application.Current?.PlatformSettings is { } settings)
        {
            settings.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(ApplyPalette);
        }

        ApplyPalette();
    }

    /// <summary>
    /// Hides the panel when the user moves on, as a popover would.
    /// </summary>
    /// <remarks>
    /// Only after the window has actually been active once: a desktop that
    /// never grants activation would otherwise hide the panel the instant it
    /// appeared. Open pop-ups keep the panel, since dismissing them is what
    /// took the focus. A focus loss in the first moments after the panel
    /// appears is treated as the shell finishing its own click rather than
    /// as the user moving on; see <see cref="FocusGracePeriod"/>.
    /// </remarks>
    /// <param name="sender">The window.</param>
    /// <param name="e">Unused.</param>
    private void HandleDeactivated(object? sender, EventArgs e)
    {
        if (!_wasActivated || _volumePopup.IsOpen)
        {
            return;
        }

        if (!_reclaimedFocus && DateTimeOffset.UtcNow - _shownAt < FocusGracePeriod)
        {
            _reclaimedFocus = true;
            Activate();
            return;
        }

        HidePanel();
    }

    /// <summary>
    /// Lets Escape dismiss the panel.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">Key event.</param>
    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HidePanel();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Turns a close request into a hide unless quitting.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">Close event, cancelled to hide instead.</param>
    private void HandleClosing(object? sender, WindowClosingEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        HidePanel();
    }

    /// <summary>
    /// Stops the timers and releases the view model on a real close.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">Unused.</param>
    private void HandleClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _progressTimer.Stop();
        _viewModel.PropertyChanged -= HandleViewModelChanged;
        _preferences.Changed -= HandlePreferencesChanged;
        _viewModel.Dispose();
        _artworkBitmap?.Dispose();
    }

    /// <summary>
    /// Lets the user drag the borderless panel by any empty area.
    /// </summary>
    /// <remarks>
    /// Buttons and sliders mark their presses handled, so a press that
    /// reaches here landed on inert content.
    /// </remarks>
    /// <param name="sender">The root border.</param>
    /// <param name="e">Pointer event.</param>
    private void HandleRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || !e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>
    /// Marshals a view model change onto the UI thread and redraws.
    /// </summary>
    /// <remarks>
    /// The view model raises its event from whichever thread completed its
    /// backend call, so the update is posted rather than applied directly.
    /// Which property changed is ignored: <see cref="UpdateView"/> rewrites
    /// the whole panel, and posting one redraw per notification coalesces
    /// naturally.
    /// </remarks>
    /// <param name="sender">The view model.</param>
    /// <param name="e">Name of the changed property; unused.</param>
    private void HandleViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateView);
    }

    /// <summary>
    /// Repaints after a settings change.
    /// </summary>
    /// <param name="sender">The preferences store.</param>
    /// <param name="e">Unused.</param>
    private void HandlePreferencesChanged(object? sender, EventArgs e)
    {
        ApplyPalette();
        UpdateView();
    }

    /// <summary>
    /// Resolves the palette for the current theme and pushes it to every control.
    /// </summary>
    /// <remarks>
    /// Also sets the window's theme variant, so the settings menu and tooltips
    /// - which come from the Fluent theme - match the panel rather than the
    /// desktop. Rounded corners need a see-through window; where the desktop
    /// grants none, the corners are squared off and the fallback fill is
    /// matched to the panel, since Avalonia would otherwise paint the area
    /// outside the rounding white.
    /// </remarks>
    private void ApplyPalette()
    {
        var colours = Application.Current?.PlatformSettings?.GetColorValues();
        var systemIsDark = colours?.ThemeVariant == PlatformThemeVariant.Dark;
        var accent = colours?.AccentColor1 ?? Colors.DodgerBlue;
        if (accent.A == 0)
        {
            accent = Colors.DodgerBlue;
        }

        var translucency = ActualTransparencyLevel == WindowTransparencyLevel.Blur
            || ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur
            || ActualTransparencyLevel == WindowTransparencyLevel.Mica
                ? PanelTranslucency.Blurred
                : ActualTransparencyLevel == WindowTransparencyLevel.Transparent
                    ? PanelTranslucency.Transparent
                    : PanelTranslucency.None;

        _palette = PanelPalette.Resolve(
            _preferences.Current.PanelTheme,
            systemIsDark,
            accent,
            translucency);
        RequestedThemeVariant = _palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var primary = new SolidColorBrush(_palette.Primary);
        var secondary = new SolidColorBrush(_palette.Secondary);
        var control = new SolidColorBrush(_palette.Control);
        var controlPressed = new SolidColorBrush(_palette.ControlPressed);
        var separator = new SolidColorBrush(_palette.Separator);
        var track = new SolidColorBrush(_palette.Track);
        var accentBrush = new SolidColorBrush(_palette.Accent);

        _root.Background = new SolidColorBrush(_palette.Background);
        _root.BorderThickness = new Thickness(_preferences.Current.PanelTheme == PanelTheme.Liquid ? 1 : 0);
        _root.BorderBrush = new SolidColorBrush(PanelPalette.WithAlpha(_palette.Primary, 0.08));
        _root.CornerRadius = new CornerRadius(translucency == PanelTranslucency.None ? 0 : PanelCornerRadius);
        TransparencyBackgroundFallback = new SolidColorBrush(PanelPalette.WithAlpha(_palette.Background, 1));

        _artworkPlaceholder.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(PanelPalette.WithAlpha(_palette.Accent, 0.68), 0),
                new GradientStop(PanelPalette.WithAlpha(_palette.Accent, 0.24), 1),
            },
        };
        _artworkGlyph.Foreground = new SolidColorBrush(PanelPalette.WithAlpha(Colors.White, 0.9));

        _title.Foreground = primary;
        _title.AutomaticallyScrolls = _preferences.Current.AutomaticallyScrollsTitles;
        _title.PointsPerSecond = _preferences.Current.MarqueePointsPerSecond;
        _artist.Foreground = secondary;
        _playerLogo.Foreground = primary;

        foreach (var button in new[] { _previousButton, _playPauseButton, _nextButton })
        {
            button.Foreground = control;
            button.PressedForeground = controlPressed;
        }

        _seekSlider.TrackBrush = track;
        _seekSlider.FillBrush = accentBrush;
        _leadingTime.Foreground = secondary;
        _trailingTime.Foreground = secondary;

        _emptyIcon.Foreground = secondary;
        _emptyTitle.Foreground = primary;
        _emptyDescription.Foreground = secondary;
        _errorText.Foreground = primary;
        _lyricsView.Foreground = primary;
        _lyricsView.SeparatorBrush = separator;

        _footerRule.Background = separator;
        _versionText.Foreground = secondary;
        foreach (var button in new[] { _volumeButton, _settingsButton, _exitButton, _muteButton })
        {
            button.Foreground = secondary;
            button.PressedForeground = new SolidColorBrush(PanelPalette.WithAlpha(_palette.Primary, 0.35));
        }

        _volumePanel.Background = new SolidColorBrush(_palette.FloatingBackground);
        _volumePanel.BorderBrush = separator;
        _volumeSlider.TrackBrush = track;
        _volumeSlider.FillBrush = accentBrush;
        _volumeValue.Foreground = secondary;
    }

    /// <summary>
    /// Rewrites every control from the current view model state.
    /// </summary>
    /// <remarks>
    /// Chooses between the card and the empty state, then fills whichever is
    /// showing. Must be called on the UI thread.
    /// </remarks>
    private void UpdateView()
    {
        var session = _viewModel.ActiveSession;
        var error = _viewModel.ErrorText;

        if (session is not null)
        {
            _body.Content = _card;
            UpdateCard(session);
        }
        else
        {
            _body.Content = _empty;
            UpdateEmpty(error);
        }

        _errorBanner.IsVisible = session is not null && error is not null;
        _errorText.Text = error ?? string.Empty;

        var lyrics = session is null ? null : _viewModel.Lyrics;
        _lyricsView.Lyrics = lyrics;
        _lyricsView.IsVisible = lyrics is not null;

        var volume = _viewModel.Volume;
        _volumeButton.Icon = PanelIcons.Speaker(volume);
        _volumeButton.IsEnabled = _viewModel.HasVolume;
        _muteButton.Icon = PanelIcons.Speaker(volume);
        if (!_volumeSlider.IsDragging)
        {
            _volumeSlider.Value = volume;
        }

        _volumeValue.Text = volume.ToString();
        ToolTip.SetTip(_volumeButton, $"{session?.PlayerName ?? "플레이어"} 음량 {volume}%");
        ToolTip.SetTip(_muteButton, volume > 0 ? "음소거" : "음소거 해제");
        if (!_viewModel.HasVolume)
        {
            _volumePopup.IsOpen = false;
        }

        UpdateArtwork(_viewModel.ArtworkData);
        UpdateProgress();
    }

    /// <summary>
    /// Fills the card from a session.
    /// </summary>
    /// <remarks>
    /// The transport stays enabled whenever there is a session, including
    /// after a failed command: the macOS panel only disables it when the
    /// player itself is unreachable, which here means there is no session
    /// at all.
    /// </remarks>
    /// <param name="session">Session on display.</param>
    private void UpdateCard(MediaSessionSnapshot session)
    {
        _title.Title = string.IsNullOrWhiteSpace(session.Title) ? "제목 없음" : session.Title;
        _artist.Text = session.Artist;
        _artist.IsVisible = !string.IsNullOrEmpty(session.Artist);

        var logo = _viewModel.PlayerLogo;
        _playerLogo.Icon = logo switch
        {
            PanelPlayerLogo.Spotify => PanelIcons.SpotifyLogo,
            PanelPlayerLogo.YouTubeMusic => PanelIcons.YouTubeMusicLogo,
            _ => PanelIcons.MusicNote,
        };
        _playerLogo.IconSize = logo == PanelPlayerLogo.Generic ? 17 : 21;
        ToolTip.SetTip(_playerLogo, session.PlayerName);
        _artworkGlyph.Icon = logo switch
        {
            PanelPlayerLogo.Spotify => PanelIcons.WaveformCircle,
            PanelPlayerLogo.YouTubeMusic => PanelIcons.PlayRectangle,
            _ => PanelIcons.MusicNote,
        };

        var playing = _viewModel.IsPlaying;
        _playPauseButton.Icon = playing ? PanelIcons.Pause : PanelIcons.Play;
        ToolTip.SetTip(_playPauseButton, playing ? "일시정지" : "재생");

        var duration = _viewModel.Duration;
        _seekSlider.Maximum = Math.Max(duration.TotalSeconds, 1);
        _seekSlider.IsEnabled = duration > TimeSpan.Zero;
    }

    /// <summary>
    /// Fills the empty state for a missing player or a failed poll.
    /// </summary>
    /// <param name="error">Poll error, or null when there simply is no player.</param>
    private void UpdateEmpty(string? error)
    {
        if (error is not null)
        {
            _emptyIcon.Icon = PanelIcons.Warning;
            _emptyTitle.Text = "플레이어에 접근할 수 없음";
            _emptyDescription.Text = error;
        }
        else
        {
            _emptyIcon.Icon = PanelIcons.Power;
            _emptyTitle.Text = "실행 중인 플레이어 없음";
            _emptyDescription.Text = "MPRIS를 지원하는 앱을 실행하고 음악을 재생하면 여기에 표시됩니다.";
        }
    }

    /// <summary>
    /// Swaps the decoded cover when the artwork bytes change.
    /// </summary>
    /// <remarks>
    /// Decodes at twice the display size, which is enough for a HiDPI panel
    /// and far smaller than the originals players hand out. Undecodable data
    /// falls back to the placeholder rather than an error.
    /// </remarks>
    /// <param name="bytes">Encoded artwork, or null for none.</param>
    private void UpdateArtwork(byte[]? bytes)
    {
        if (ReferenceEquals(bytes, _artworkBytes))
        {
            return;
        }

        _artworkBytes = bytes;
        _artworkBitmap?.Dispose();
        _artworkBitmap = null;

        if (bytes is not null)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                _artworkBitmap = Bitmap.DecodeToWidth(stream, (int)Math.Max(_artworkFrame.Width * 2, MinimumArtworkSize * 2));
            }
            catch (Exception)
            {
                _artworkBitmap = null;
            }
        }

        _artworkImage.Source = _artworkBitmap;
        _artworkImage.IsVisible = _artworkBitmap is not null;
    }

    /// <summary>
    /// Advances the scrubber and time labels between polls.
    /// </summary>
    /// <remarks>
    /// Runs four times a second so a playing track appears to move
    /// continuously even though the player is only sampled once a second.
    /// </remarks>
    private void UpdateProgress()
    {
        if (_viewModel.ActiveSession is null || !IsVisible)
        {
            return;
        }

        var duration = _viewModel.Duration;
        var position = _viewModel.DisplayedPosition();
        var remaining = duration > position ? duration - position : TimeSpan.Zero;

        if (!_seekSlider.IsDragging)
        {
            _seekSlider.Value = position.TotalSeconds;
        }

        _lyricsView.Advance(position);

        _leadingTime.Text = PanelTimeDisplay.LeadingText(
            _preferences.Current.LeadingTimeStyle,
            position);
        _trailingTime.Text = PanelTimeDisplay.TrailingText(
            _preferences.Current.TrailingTimeStyle,
            duration,
            remaining);
    }

    /// <summary>
    /// Opens or closes the floating volume slider.
    /// </summary>
    private void ToggleVolumePopup()
    {
        if (_volumePopup.IsOpen)
        {
            _volumePopup.IsOpen = false;
            return;
        }

        _volumeSlider.Value = _viewModel.Volume;
        _volumePopup.IsOpen = true;
    }
}
