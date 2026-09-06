using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// The settings window, laid out like the macOS <c>RepriseSettingsView</c>.
/// </summary>
/// <remarks>
/// Six tabs at the same 500 by 480 size: General, YouTube Music, Theme,
/// Menu bar, Panel, and System info, each a grouped form with the same
/// sections, captions, and footnotes as the Mac. The tab strip is drawn by
/// <see cref="SettingsTabBar"/> rather than a <c>TabControl</c>, which
/// left-aligns and accent-tints its items in a way the Mac toolbar does not. Where a Mac feature has no
/// Linux equivalent, the section explains what Linux does instead rather
/// than disappearing, so the two windows stay recognisably the same.
/// <para>
/// One instance lives for the whole session and hides instead of closing,
/// so reopening it is instant and keeps the selected tab.
/// </para>
/// </remarks>
public sealed class SettingsWindow : Window
{
    private const double PriorityRowHeight = 39;

    private readonly NowPlayingViewModel _viewModel;
    private readonly PreferencesStore _preferences;
    private readonly AutostartEntry _autostart = new();
    private readonly DispatcherTimer _sessionTimer;
    private readonly List<Action> _refreshers = [];
    private StackPanel? _sessionList;
    private StackPanel? _priorityList;
    private TextBlock? _autostartError;
    private ToggleSwitch? _autostartToggle;

    /// <summary>
    /// Whether the application is shutting down and the window may close.
    /// </summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// Builds the window and its tabs.
    /// </summary>
    /// <param name="viewModel">Playback state for the sessions list.</param>
    /// <param name="preferences">Settings to show and edit.</param>
    /// <example>
    /// <code>
    /// var settings = new SettingsWindow(viewModel, preferences);
    /// settings.Present();
    /// </code>
    /// </example>
    public SettingsWindow(NowPlayingViewModel viewModel, PreferencesStore preferences)
    {
        _viewModel = viewModel;
        _preferences = preferences;

        Title = "Reprise 설정";
        Width = 500;
        Height = 480;
        CanResize = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ApplyPalette();

        var pages = new Control[]
        {
            BuildGeneralTab(),
            BuildYouTubeMusicTab(),
            BuildThemeTab(),
            BuildMenuBarTab(),
            BuildPanelTab(),
            BuildSystemInfoTab(),
        };
        var body = new ContentControl
        {
            Content = pages[0],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var tabBar = new SettingsTabBar(
        [
            ("일반", PanelIcons.Gear),
            ("YouTube Music", PanelIcons.PlayCircle),
            ("테마", PanelIcons.Palette),
            ("메뉴바", PanelIcons.MenuBarRectangle),
            ("패널", PanelIcons.PlayRectangle),
            ("시스템 정보", PanelIcons.InfoSquare),
        ]);
        tabBar.SelectionChanged += (_, index) => body.Content = pages[index];

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { tabBar, body },
        };
        Grid.SetRow(body, 1);
        Content = layout;

        _sessionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(500),
            DispatcherPriority.Background,
            (_, _) => RefreshSessions());

        _preferences.Changed += (_, _) => RefreshAll();
        Opened += (_, _) =>
        {
            if (Application.Current?.PlatformSettings is { } settings)
            {
                settings.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(ApplyPalette);
            }

            ApplyPalette();
            RefreshAll();
            _sessionTimer.Start();
        };
        Closing += (_, e) =>
        {
            if (AllowClose)
            {
                return;
            }

            e.Cancel = true;
            _sessionTimer.Stop();
            Hide();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>
    /// Shows the window, or brings it forward if already open.
    /// </summary>
    public void Present()
    {
        Show();
        Activate();
        _sessionTimer.Start();
        RefreshAll();
    }

    /// <summary>
    /// Publishes the window's colours, following the desktop appearance.
    /// </summary>
    /// <remarks>
    /// Every control binds to these resources, so replacing them repaints
    /// the whole window without rebuilding a single tab.
    /// </remarks>
    private void ApplyPalette()
    {
        var colours = Application.Current?.PlatformSettings?.GetColorValues();
        var accent = colours?.AccentColor1 is { A: > 0 } value ? value : Colors.DodgerBlue;
        var palette = SettingsPalette.Resolve(colours?.ThemeVariant == PlatformThemeVariant.Dark, accent);
        RequestedThemeVariant = colours?.ThemeVariant == PlatformThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        var brushes = palette.Brushes;
        for (var index = 0; index < SettingsPalette.ResourceKeys.Length; index++)
        {
            Resources[SettingsPalette.ResourceKeys[index]] = brushes[index];
        }

        Background = brushes[0];
    }

    /// <summary>
    /// Runs every registered refresher, on the UI thread.
    /// </summary>
    private void RefreshAll()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshAll);
            return;
        }

        foreach (var refresh in _refreshers)
        {
            refresh();
        }
    }

    /// <summary>
    /// Current settings, shortened for the builders below.
    /// </summary>
    private DesktopPreferences Current => _preferences.Current;

    /// <summary>
    /// Applies a settings change.
    /// </summary>
    /// <param name="change">Produces the new settings.</param>
    private void Update(Func<DesktopPreferences, DesktopPreferences> change) => _preferences.Update(change);

    /// <summary>
    /// The General tab: playback, tray lyrics, display priority, autostart.
    /// </summary>
    /// <returns>The page.</returns>
    private Control BuildGeneralTab()
    {
        var (autoPauseRow, autoPause) = SettingsForm.Toggle(
            "다른 플레이어 자동 정지",
            Current.AutomaticallyPausesOtherPlayer,
            on => Update(p => p with { AutomaticallyPausesOtherPlayer = on }));

        var (lyricsRow, lyrics) = SettingsForm.Toggle(
            "가사 표시",
            Current.MenuBarShowsLyrics,
            on => Update(p => p with { MenuBarShowsLyrics = on }));
        var widthSlider = new Slider
        {
            Minimum = 10,
            Maximum = 60,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 160,
            Value = Current.MenuBarLabelLength,
        };
        var widthValue = new TextBlock { FontSize = PanelTypography.Small, Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        widthValue.Bind(TextBlock.ForegroundProperty, widthValue.GetResourceObservable("SettingsSecondaryBrush"));
        widthSlider.ValueChanged += (_, _) =>
        {
            var length = (int)Math.Round(widthSlider.Value);
            widthValue.Text = $"{length}자";
            if (length != Current.MenuBarLabelLength)
            {
                Update(p => p with { MenuBarLabelLength = length });
            }
        };
        var widthRow = SettingsForm.Row("가사 영역 너비", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { widthSlider, widthValue },
        });
        var (reserveRow, reserve) = SettingsForm.Toggle(
            "가사 공간 확보",
            Current.MenuBarReservesLabelWidth,
            on => Update(p => p with { MenuBarReservesLabelWidth = on }));

        var (rememberRow, remember) = SettingsForm.Toggle(
            "마지막에 재생한 플레이어 기억",
            Current.RemembersLastPlayedPlayer,
            on => Update(p => p with { RemembersLastPlayedPlayer = on }));
        _priorityList = new StackPanel();

        var (autostartRow, autostartToggle) = SettingsForm.Toggle(
            "로그인 시 Reprise 자동 실행",
            _autostart.IsEnabled,
            on =>
            {
                var error = _autostart.SetEnabled(on);
                if (_autostartError is not null)
                {
                    _autostartError.Text = error ?? string.Empty;
                    _autostartError.IsVisible = error is not null;
                }
            },
            _autostart.IsAvailable);
        _autostartToggle = autostartToggle;
        _autostartError = new TextBlock
        {
            FontSize = PanelTypography.Subtitle,
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 0, 14, 8),
            IsVisible = false,
        };

        _refreshers.Add(() =>
        {
            var current = Current;
            autoPause.IsChecked = current.AutomaticallyPausesOtherPlayer;
            lyrics.IsChecked = current.MenuBarShowsLyrics;
            widthSlider.Value = current.MenuBarLabelLength;
            widthValue.Text = $"{current.MenuBarLabelLength}자";
            widthSlider.IsEnabled = current.MenuBarShowsLyrics;
            reserve.IsChecked = current.MenuBarReservesLabelWidth;
            reserve.IsEnabled = current.MenuBarShowsLyrics;
            remember.IsChecked = current.RemembersLastPlayedPlayer;
            RebuildPriorityList();
        });

        return SettingsForm.Page(
            SettingsForm.Section(
                "재생",
                "한 플레이어가 재생을 시작하면 기존에 재생 중이던 다른 플레이어를 일시 정지합니다.",
                autoPauseRow),
            SettingsForm.Section(
                "제어 목록",
                "영역 너비는 상단 바에서 가사가 차지할 최대 글자 수입니다. 공간 확보를 켜면 선택한 너비로 고정해 주변 항목이 움직이지 않게 합니다.",
                lyricsRow,
                widthRow,
                reserveRow),
            SettingsForm.Section(
                "표시 우선순위",
                "기억을 켜면 마지막으로 재생을 시작한 플레이어를 우선 표시합니다. 그 외에는 아래 순서를 사용하며, 항목을 드래그하여 변경할 수 있습니다.",
                rememberRow,
                _priorityList),
            SettingsForm.Section(
                "자동 실행",
                "로그인하면 Reprise를 자동으로 실행합니다. 항목은 ~/.config/autostart에 저장됩니다.",
                autostartRow,
                _autostartError));
    }

    /// <summary>
    /// Rebuilds the draggable priority rows from the current ordering.
    /// </summary>
    private void RebuildPriorityList()
    {
        if (_priorityList is null)
        {
            return;
        }

        _priorityList.Children.Clear();
        var order = PlayerPriority.Parse(Current.PlayerDisplayPriority);
        for (var index = 0; index < order.Count; index++)
        {
            if (index > 0)
            {
                _priorityList.Children.Add(SettingsForm.Hairline());
            }

            _priorityList.Children.Add(PriorityRow(order, index));
        }
    }

    /// <summary>
    /// One row of the priority list, draggable to a new position.
    /// </summary>
    /// <param name="order">Current ordering.</param>
    /// <param name="index">Row being built.</param>
    /// <returns>The row.</returns>
    private Control PriorityRow(IReadOnlyList<PanelPlayerLogo> order, int index)
    {
        var kind = order[index];
        var number = new TextBlock { Text = (index + 1).ToString(), Width = 16, FontSize = PanelTypography.Body, VerticalAlignment = VerticalAlignment.Center };
        number.Bind(TextBlock.ForegroundProperty, number.GetResourceObservable("SettingsSecondaryBrush"));
        var icon = new PanelGlyph
        {
            Icon = kind switch
            {
                PanelPlayerLogo.Spotify => PanelIcons.SpotifyLogo,
                PanelPlayerLogo.YouTubeMusic => PanelIcons.YouTubeMusicLogo,
                _ => PanelIcons.MusicNote,
            },
            IconSize = 16,
            Width = 18,
            Height = 18,
        };
        icon.Bind(PanelGlyph.ForegroundProperty, icon.GetResourceObservable("SettingsPrimaryBrush"));
        var handle = new TextBlock { Text = "☰", FontSize = PanelTypography.Body, VerticalAlignment = VerticalAlignment.Center };
        handle.Bind(TextBlock.ForegroundProperty, handle.GetResourceObservable("SettingsSecondaryBrush"));

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 10,
            Height = PriorityRowHeight,
            Margin = new Thickness(14, 0),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            Children =
            {
                number,
                icon,
                new TextBlock { Text = PlayerPriority.DisplayName(kind), FontSize = PanelTypography.Body, VerticalAlignment = VerticalAlignment.Center },
                handle,
            },
        };
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(row.Children[2], 2);
        Grid.SetColumn(handle, 3);

        var startY = 0.0;
        var dragging = false;
        row.PointerPressed += (_, e) =>
        {
            startY = e.GetPosition(_priorityList).Y;
            dragging = true;
            e.Pointer.Capture(row);
            row.ZIndex = 1;
            e.Handled = true;
        };
        row.PointerMoved += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            var offset = e.GetPosition(_priorityList).Y - startY;
            row.RenderTransform = new TranslateTransform(0, offset);
        };
        row.PointerReleased += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            e.Pointer.Capture(null);
            row.RenderTransform = null;
            row.ZIndex = 0;
            var offset = e.GetPosition(_priorityList).Y - startY;
            var target = Math.Clamp((int)Math.Round(index + offset / PriorityRowHeight), 0, order.Count - 1);
            if (target != index)
            {
                var moved = PlayerPriority.Move(order, kind, target);
                Update(p => p with { PlayerDisplayPriority = PlayerPriority.Serialize(moved) });
            }
        };
        return row;
    }

    /// <summary>
    /// The YouTube Music tab: browser sessions seen over MPRIS.
    /// </summary>
    /// <remarks>
    /// On macOS this tab lists the browser extension's connections. Linux
    /// browsers publish their media sessions over MPRIS themselves, so the
    /// same list is filled from the players on the bus and no extension is
    /// offered.
    /// </remarks>
    /// <returns>The page.</returns>
    private Control BuildYouTubeMusicTab()
    {
        _sessionList = new StackPanel();
        _refreshers.Add(RefreshSessions);
        return SettingsForm.Page(
            SettingsForm.Section(
                "YouTube Music 세션",
                "브라우저에서 열린 각 YouTube Music 탭을 표시합니다. 재생 상태와 표시 우선순위를 기준으로 제어할 세션을 자동 선택합니다.",
                _sessionList),
            SettingsForm.Section(
                "브라우저 확장",
                "Linux에서는 Chromium과 Firefox가 재생 중인 탭을 MPRIS로 직접 노출하므로 별도의 확장 프로그램이 필요하지 않습니다. YouTube Music 탭에서 재생을 시작하면 자동으로 연결됩니다.",
                SettingsForm.Value("확장 프로그램", "필요 없음")));
    }

    /// <summary>
    /// Rewrites the sessions list from the latest poll.
    /// </summary>
    private void RefreshSessions()
    {
        if (_sessionList is null || !IsVisible)
        {
            return;
        }

        var sessions = _viewModel.Sessions
            .Where(session => IsBrowserSession(session.PlayerId))
            .ToList();
        _sessionList.Children.Clear();
        if (sessions.Count == 0)
        {
            var empty = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(14, 10),
            };
            var glyph = new PanelGlyph { Icon = PanelIcons.Power, IconSize = 16, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Top };
            glyph.Bind(PanelGlyph.ForegroundProperty, glyph.GetResourceObservable("SettingsSecondaryBrush"));
            var text = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "연결된 세션 없음", FontSize = PanelTypography.Body },
                    SettingsForm.Footnote("브라우저에서 YouTube Music을 열고 재생을 시작해 주세요."),
                },
            };
            ((TextBlock)text.Children[1]).Margin = new Thickness(0);
            empty.Children.Add(glyph);
            empty.Children.Add(text);
            _sessionList.Children.Add(empty);
            return;
        }

        var active = _viewModel.ActiveSession?.PlayerId;
        for (var index = 0; index < sessions.Count; index++)
        {
            if (index > 0)
            {
                _sessionList.Children.Add(SettingsForm.Hairline());
            }

            _sessionList.Children.Add(SessionRow(sessions[index], sessions[index].PlayerId == active));
        }
    }

    /// <summary>
    /// One row describing a browser session.
    /// </summary>
    /// <param name="session">Session to describe.</param>
    /// <param name="isActive">Whether it is the one on display.</param>
    /// <returns>The row.</returns>
    private static Control SessionRow(MediaSessionSnapshot session, bool isActive)
    {
        var glyph = new PanelGlyph { Icon = PanelIcons.PlayRectangle, IconSize = 16, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        glyph.Bind(PanelGlyph.ForegroundProperty, glyph.GetResourceObservable(isActive ? "SettingsAccentBrush" : "SettingsSecondaryBrush"));

        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        name.Children.Add(new TextBlock { Text = session.PlayerName, FontSize = PanelTypography.Body, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });
        if (isActive)
        {
            var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2) };
            badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable("SettingsWellBrush"));
            var badgeText = new TextBlock { Text = "사용 중", FontSize = PanelTypography.Caption, FontWeight = FontWeight.SemiBold };
            badgeText.Bind(TextBlock.ForegroundProperty, badgeText.GetResourceObservable("SettingsAccentBrush"));
            badge.Child = badgeText;
            name.Children.Add(badge);
        }

        var track = string.IsNullOrEmpty(session.Artist) ? session.Title : $"{session.Title} · {session.Artist}";
        var detail = session.Status switch
        {
            PlaybackStatus.Playing => track,
            PlaybackStatus.Paused => $"일시 정지 · {track}",
            PlaybackStatus.Stopped => $"정지 · {track}",
            _ => string.IsNullOrEmpty(track) ? "재생 정보 없음" : track,
        };
        var detailText = new TextBlock { Text = detail, FontSize = PanelTypography.Subtitle, TextTrimming = TextTrimming.CharacterEllipsis };
        detailText.Bind(TextBlock.ForegroundProperty, detailText.GetResourceObservable("SettingsSecondaryBrush"));

        var state = new PanelGlyph
        {
            Icon = session.Status == PlaybackStatus.Playing ? PanelIcons.Play : PanelIcons.Pause,
            IconSize = 12,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = session.Status == PlaybackStatus.Playing ? Brushes.MediumSeaGreen : Brushes.Gray,
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(14, 8),
            Children =
            {
                glyph,
                new StackPanel { Spacing = 3, Children = { name, detailText } },
                state,
            },
        };
        Grid.SetColumn(row.Children[1], 1);
        Grid.SetColumn(state, 2);
        return row;
    }

    /// <summary>
    /// Whether a player id belongs to a browser or a YouTube Music wrapper.
    /// </summary>
    /// <param name="playerId">MPRIS player id.</param>
    /// <returns>True for the players this tab lists.</returns>
    private static bool IsBrowserSession(string playerId)
    {
        if (NowPlayingViewModel.LogoFor(playerId) == PanelPlayerLogo.YouTubeMusic)
        {
            return true;
        }

        return new[] { "chromium", "chrome", "firefox", "brave", "edge", "opera", "vivaldi", "zen", "librewolf" }
            .Any(browser => playerId.Contains(browser, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The Theme tab: a live panel preview and the theme picker.
    /// </summary>
    /// <returns>The page.</returns>
    private Control BuildThemeTab()
    {
        var preview = new ThemePanelPreview { Margin = new Thickness(0, 10) };
        var picker = new SegmentedPicker(
            Enum.GetValues<PanelTheme>().Select(theme => (theme.DisplayName(), (object)theme)),
            Current.PanelTheme);
        picker.SelectionChanged += (_, value) => Update(p => p with { PanelTheme = (PanelTheme)value });

        _refreshers.Add(() =>
        {
            var colours = Application.Current?.PlatformSettings?.GetColorValues();
            preview.Update(
                Current.PanelTheme,
                Current,
                colours?.ThemeVariant == PlatformThemeVariant.Dark,
                colours?.AccentColor1 is { A: > 0 } accent ? accent : Colors.DodgerBlue);
            picker.SelectedValue = Current.PanelTheme;
        });

        return SettingsForm.Page(
            SettingsForm.Section("미리보기", null, SettingsForm.Fill(preview)),
            SettingsForm.Section("플레이어 패널", null, SettingsForm.Picker(null, picker)));
    }

    /// <summary>
    /// The Menu bar tab: a tray preview, text and icon choices, and the marquee.
    /// </summary>
    /// <returns>The page.</returns>
    private Control BuildMenuBarTab()
    {
        var preview = new MenuBarPreview { Margin = new Thickness(0, 10) };
        var formatPicker = new SegmentedPicker(
            Enum.GetValues<MenuBarTitleFormat>().Select(format => (format.DisplayName(), (object)format)),
            Current.MenuBarTitleFormat);
        formatPicker.SelectionChanged += (_, value) => Update(p =>
        {
            var format = (MenuBarTitleFormat)value;
            var style = format == MenuBarTitleFormat.Hidden && p.MenuBarArtworkStyle == MenuBarArtworkStyle.Hidden
                ? MenuBarArtworkStyle.AlbumArtwork
                : p.MenuBarArtworkStyle;
            return p with { MenuBarTitleFormat = format, MenuBarArtworkStyle = style };
        });
        var artworkPicker = new SegmentedPicker(
            Enum.GetValues<MenuBarArtworkStyle>().Select(style => (style.DisplayName(), (object)style)),
            Current.MenuBarArtworkStyle);
        artworkPicker.SelectionChanged += (_, value) => Update(p =>
        {
            var style = (MenuBarArtworkStyle)value;
            var format = style == MenuBarArtworkStyle.Hidden && p.MenuBarTitleFormat == MenuBarTitleFormat.Hidden
                ? MenuBarTitleFormat.TitleOnly
                : p.MenuBarTitleFormat;
            return p with { MenuBarArtworkStyle = style, MenuBarTitleFormat = format };
        });

        var (scrollRow, scroll) = SettingsForm.Toggle(
            "긴 곡 제목 캐러셀 움직이기",
            Current.AutomaticallyScrollsTitles,
            on => Update(p => p with { AutomaticallyScrollsTitles = on }));
        var speedPicker = new SegmentedPicker(
            new[] { ("느리게", (object)20.0), ("보통", 30.0), ("빠르게", 45.0) },
            Current.MarqueePointsPerSecond);
        speedPicker.SelectionChanged += (_, value) => Update(p => p with { MarqueePointsPerSecond = (double)value });
        var speedRow = SettingsForm.Picker("캐러셀 속도", speedPicker);
        var (resetRow, reset) = SettingsForm.Toggle(
            "패널을 열면 제목을 처음으로 되돌리기",
            Current.ResetsMenuTitleWhenPanelOpens,
            on => Update(p => p with { ResetsMenuTitleWhenPanelOpens = on }));

        _refreshers.Add(() =>
        {
            var current = Current;
            preview.Update(current);
            formatPicker.SelectedValue = current.MenuBarTitleFormat;
            artworkPicker.SelectedValue = current.MenuBarArtworkStyle;
            scroll.IsChecked = current.AutomaticallyScrollsTitles;
            speedPicker.SelectedValue = current.MarqueePointsPerSecond;
            reset.IsChecked = current.ResetsMenuTitleWhenPanelOpens;
            var hidden = current.MenuBarTitleFormat == MenuBarTitleFormat.Hidden;
            scrollRow.IsEnabled = !hidden;
            speedRow.IsEnabled = !hidden && current.AutomaticallyScrollsTitles;
            resetRow.IsEnabled = !hidden && current.AutomaticallyScrollsTitles;
        });

        return SettingsForm.Page(
            SettingsForm.Section("미리보기", null, SettingsForm.Fill(preview)),
            SettingsForm.Section(
                "현재 재생 중인 곡",
                null,
                SettingsForm.Picker("텍스트 내용", formatPicker),
                SettingsForm.Picker("제목 왼쪽 표시", artworkPicker)),
            SettingsForm.Section("캐러셀", null, scrollRow, speedRow, resetRow));
    }

    /// <summary>
    /// The Panel tab: a time-label preview and the two time styles.
    /// </summary>
    /// <returns>The page.</returns>
    private Control BuildPanelTab()
    {
        var preview = new PanelTimePreview { Margin = new Thickness(0, 10) };
        var leading = new SegmentedPicker(
            Enum.GetValues<PanelLeadingTimeStyle>().Select(style => (style.DisplayName(), (object)style)),
            Current.LeadingTimeStyle);
        leading.SelectionChanged += (_, value) => Update(p => p with { LeadingTimeStyle = (PanelLeadingTimeStyle)value });
        var trailing = new SegmentedPicker(
            Enum.GetValues<PanelTrailingTimeStyle>().Select(style => (style.DisplayName(), (object)style)),
            Current.TrailingTimeStyle);
        trailing.SelectionChanged += (_, value) => Update(p => p with { TrailingTimeStyle = (PanelTrailingTimeStyle)value });

        _refreshers.Add(() =>
        {
            preview.Update(Current);
            leading.SelectedValue = Current.LeadingTimeStyle;
            trailing.SelectedValue = Current.TrailingTimeStyle;
        });

        return SettingsForm.Page(
            SettingsForm.Section("미리보기", null, SettingsForm.Fill(preview)),
            SettingsForm.Section(
                "시간 표시",
                null,
                SettingsForm.Picker("왼쪽", leading),
                SettingsForm.Picker("오른쪽", trailing)));
    }

    /// <summary>
    /// The System info tab: version, updates, OS, displays, and the credit.
    /// </summary>
    /// <remarks>
    /// The credit ends in a text heart rather than the emoji the macOS
    /// window uses: the bundled typeface carries no colour emoji, and the
    /// fallback picked whichever glyph the system offered, which was not a
    /// heart.
    /// </remarks>
    /// <returns>The page.</returns>
    private Control BuildSystemInfoTab()
    {
        var displays = new StackPanel();
        _refreshers.Add(() =>
        {
            displays.Children.Clear();
            var screens = Screens.All;
            for (var index = 0; index < screens.Count; index++)
            {
                if (index > 0)
                {
                    displays.Children.Add(SettingsForm.Hairline());
                }

                var screen = screens[index];
                var name = string.IsNullOrWhiteSpace(screen.DisplayName) ? $"디스플레이 {index + 1}" : screen.DisplayName;
                displays.Children.Add(SettingsForm.Value(name, $"{screen.Bounds.Width} × {screen.Bounds.Height}"));
            }
        });

        var heart = SettingsForm.Footnote(" with ♥");
        heart.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x55));
        var credit = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                SettingsForm.Footnote("Made by "),
                SettingsForm.Link("aodjo", new Uri("https://junx.dev")),
                heart,
            },
        };
        foreach (var child in credit.Children.OfType<TextBlock>())
        {
            child.Margin = new Thickness(0);
            child.FontSize = PanelTypography.Subtitle;
        }

        var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var form = SettingsForm.Page(
            SettingsForm.Section("앱", null, SettingsForm.Value("버전", VersionText())),
            SettingsForm.Section(
                "업데이트",
                "Linux 빌드는 GitHub 릴리스로 배포됩니다. 확인을 누르면 최신 릴리스 페이지를 엽니다.",
                SettingsForm.Row("업데이트 확인…", SettingsForm.Link("릴리스 열기", new Uri("https://github.com/aodjo/Reprise/releases/latest")))),
            SettingsForm.Section("시스템", null, SettingsForm.Value("운영체제", OperatingSystemText())),
            SettingsForm.Section("연결된 디스플레이", null, displays));
        page.Children.Add(form);
        page.Children.Add(credit);
        Grid.SetRow(credit, 1);
        return page;
    }

    /// <summary>
    /// The version line for the app section.
    /// </summary>
    /// <returns>Text such as <c>2.0.0-alpha.1</c>.</returns>
    private static string VersionText()
    {
        var informational = typeof(SettingsWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informational)
            ? typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "-"
            : informational.Split('+', 2)[0];
    }

    /// <summary>
    /// The distribution and kernel, as the system section shows them.
    /// </summary>
    /// <returns>Text such as <c>Ubuntu 24.04.1 LTS (Linux 6.8.0)</c>.</returns>
    private static string OperatingSystemText()
    {
        var pretty = string.Empty;
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                pretty = File.ReadLines("/etc/os-release")
                    .FirstOrDefault(line => line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))?
                    ["PRETTY_NAME=".Length..]
                    .Trim('"') ?? string.Empty;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        var kernel = $"{Environment.OSVersion.Platform switch { PlatformID.Unix => "Linux", _ => Environment.OSVersion.Platform.ToString() }} {Environment.OSVersion.Version.ToString(3)}";
        return pretty.Length > 0 ? $"{pretty} ({kernel})" : kernel;
    }
}
