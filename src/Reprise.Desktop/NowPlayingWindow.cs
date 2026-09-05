using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// The now-playing panel: artwork, track details, timeline, and transport.
/// </summary>
/// <remarks>
/// Built in code rather than XAML so the desktop assembly stays a plain
/// library with no compiled markup, which keeps the platform heads that
/// reference it free of Avalonia's XAML build steps.
/// <para>
/// The window keeps its own references to the controls it updates and pushes
/// values into them from <see cref="UpdateView"/>, instead of using bindings.
/// At this size that is both less machinery and easier to follow than a
/// binding graph.
/// </para>
/// </remarks>
public sealed class NowPlayingWindow : Window
{
    /// <summary>
    /// Secondary text colour, for labels and metadata below the title.
    /// </summary>
    private static readonly IBrush MutedBrush = Brush.Parse("#99A3B5");

    /// <summary>
    /// Reprise accent, used for the header, artwork, and progress fill.
    /// </summary>
    private static readonly IBrush AccentBrush = Brush.Parse("#8B7CFF");

    private readonly NowPlayingViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;
    private readonly TextBlock _status;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _album;
    private readonly TextBlock _player;
    private readonly TextBlock _time;
    private readonly TextBlock _error;
    private readonly ProgressBar _progress;
    private readonly Button _previousButton;
    private readonly Button _playPauseButton;
    private readonly Button _nextButton;

    /// <summary>
    /// Whether a close request should actually close the window.
    /// </summary>
    /// <remarks>
    /// False during normal use, so the close button hides the window to the
    /// tray instead. The tray's quit item sets this to true first, which is
    /// the only way the window is allowed to close for real.
    /// </remarks>
    public bool AllowClose { get; set; }

    /// <summary>
    /// Builds the panel and starts polling for playback state.
    /// </summary>
    /// <remarks>
    /// Also wires the window's lifetime to the view model's: refreshing when
    /// shown, stopping the timer and disposing the view model when the window
    /// finally closes. Because the window owns the view model's lifetime,
    /// this takes ownership of the instance passed in.
    /// </remarks>
    /// <param name="viewModel">State to display and command.</param>
    /// <example>
    /// <code>
    /// desktop.MainWindow = new NowPlayingWindow(
    ///     new NowPlayingViewModel(service));
    /// </code>
    /// </example>
    public NowPlayingWindow(NowPlayingViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "Reprise for Linux";
        Width = 440;
        Height = 530;
        MinWidth = 380;
        MinHeight = 480;
        Background = Brush.Parse("#0D0F15");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _status = CreateTextBlock(13, FontWeight.Medium, MutedBrush);
        _title = CreateTextBlock(28, FontWeight.SemiBold, Brushes.White);
        _title.TextWrapping = TextWrapping.Wrap;
        _artist = CreateTextBlock(17, FontWeight.Medium, Brush.Parse("#D8DBE5"));
        _artist.TextWrapping = TextWrapping.Wrap;
        _album = CreateTextBlock(14, FontWeight.Normal, MutedBrush);
        _album.TextWrapping = TextWrapping.Wrap;
        _player = CreateTextBlock(12, FontWeight.Medium, MutedBrush);
        _time = CreateTextBlock(12, FontWeight.Medium, MutedBrush);
        _time.HorizontalAlignment = HorizontalAlignment.Right;
        _error = CreateTextBlock(12, FontWeight.Normal, Brush.Parse("#FF9A9A"));
        _error.TextWrapping = TextWrapping.Wrap;
        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Height = 5,
            Foreground = AccentBrush,
        };

        _previousButton = CreateControlButton("이전");
        _playPauseButton = CreateControlButton("재생");
        _nextButton = CreateControlButton("다음");
        _previousButton.Click += async (_, _) =>
            await _viewModel.SendAsync(PlaybackCommand.Previous);
        _playPauseButton.Click += async (_, _) =>
            await _viewModel.SendAsync(PlaybackCommand.PlayPause);
        _nextButton.Click += async (_, _) =>
            await _viewModel.SendAsync(PlaybackCommand.Next);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += async (_, _) => await _viewModel.RefreshAsync();

        Content = BuildContent();
        _viewModel.PropertyChanged += HandleViewModelChanged;
        Opened += async (_, _) => await _viewModel.RefreshAsync();
        Closing += (_, eventArgs) =>
        {
            if (AllowClose)
            {
                return;
            }

            eventArgs.Cancel = true;
            Hide();
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _viewModel.PropertyChanged -= HandleViewModelChanged;
            _viewModel.Dispose();
        };

        _refreshTimer.Start();
        UpdateView();
    }

    /// <summary>
    /// Assembles the panel layout from the controls created in the constructor.
    /// </summary>
    /// <remarks>
    /// Laid out top to bottom as header, now-playing row, timeline,
    /// transport, and error line. The error line sits last and stays in the
    /// tree even when empty, so a message appearing does not shift the
    /// controls above it.
    /// </remarks>
    /// <returns>The window's content tree.</returns>
    private Control BuildContent()
    {
        var header = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "REPRISE  ·  LINUX",
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Foreground = AccentBrush,
                    LetterSpacing = 1.2,
                },
                _status,
            },
        };

        var artwork = new Border
        {
            Width = 124,
            Height = 124,
            CornerRadius = new CornerRadius(24),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#8B7CFF"), 0),
                    new GradientStop(Color.Parse("#4D3FAA"), 1),
                },
            },
            Child = new TextBlock
            {
                Text = "R",
                FontSize = 52,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var metadata = new StackPanel
        {
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _title,
                _artist,
                _album,
                _player,
            },
        };

        var nowPlaying = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 24,
            Children =
            {
                artwork,
                metadata,
            },
        };
        Grid.SetColumn(metadata, 1);

        var timeline = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _progress,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        CreateTextBlock(12, FontWeight.Normal, MutedBrush, "MPRIS"),
                        _time,
                    },
                },
            },
        };
        Grid.SetColumn(_time, 1);

        var controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10,
            Children =
            {
                _previousButton,
                _playPauseButton,
                _nextButton,
            },
        };
        Grid.SetColumn(_playPauseButton, 1);
        Grid.SetColumn(_nextButton, 2);

        return new Border
        {
            Padding = new Thickness(30),
            Child = new StackPanel
            {
                Spacing = 28,
                Children =
                {
                    header,
                    nowPlaying,
                    timeline,
                    controls,
                    _error,
                },
            },
        };
    }

    /// <summary>
    /// Marshals a view model change onto the UI thread and redraws.
    /// </summary>
    /// <remarks>
    /// The view model raises <see cref="INotifyPropertyChanged.PropertyChanged"/>
    /// from whichever thread completed its backend call, so the update is
    /// posted rather than applied directly. Which property changed is
    /// ignored: <see cref="UpdateView"/> rewrites the whole panel, and
    /// posting one redraw per notification coalesces naturally.
    /// </remarks>
    /// <param name="sender">The view model raising the change.</param>
    /// <param name="eventArgs">Name of the changed property; unused.</param>
    private void HandleViewModelChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(UpdateView);
    }

    /// <summary>
    /// Rewrites every control from the current view model state.
    /// </summary>
    /// <remarks>
    /// Each field falls back to placeholder text when the backend has nothing
    /// to report, so the panel reads as waiting rather than broken. Transport
    /// buttons are disabled without an active session, since there would be
    /// no player to address.
    /// <para>
    /// Must be called on the UI thread.
    /// </para>
    /// </remarks>
    private void UpdateView()
    {
        var session = _viewModel.ActiveSession;
        _status.Text = _viewModel.StatusText;
        _title.Text = string.IsNullOrWhiteSpace(session?.Title)
            ? "재생 대기 중"
            : session.Title;
        _artist.Text = session?.Artist ?? "Linux 미디어 플레이어를 실행하세요.";
        _album.Text = session?.Album ?? string.Empty;
        _player.Text = session is null
            ? "D-Bus · MPRIS"
            : $"{session.PlayerName} · MPRIS";
        _progress.Value = session?.Progress ?? 0;
        _time.Text = FormatTimeline(session);
        _error.Text = _viewModel.ErrorText ?? string.Empty;

        var enabled = session is not null;
        _previousButton.IsEnabled = enabled;
        _playPauseButton.IsEnabled = enabled;
        _nextButton.IsEnabled = enabled;
        _playPauseButton.Content = session?.Status == PlaybackStatus.Playing
            ? "일시 정지"
            : "재생";
    }

    /// <summary>
    /// Renders the elapsed and total time pair shown beside the progress bar.
    /// </summary>
    /// <remarks>
    /// Both halves are required: a player reporting one without the other - a
    /// live stream, typically - gets placeholders rather than a half-filled
    /// readout that would imply a length it never gave.
    /// </remarks>
    /// <param name="session">Session to describe, or null.</param>
    /// <returns>
    /// A <c>position / duration</c> pair, or <c>--:-- / --:--</c> when either
    /// value is missing.
    /// </returns>
    /// <example>
    /// <code>
    /// FormatTimeline(session); // "1:07 / 3:52"
    /// </code>
    /// </example>
    private static string FormatTimeline(MediaSessionSnapshot? session)
    {
        if (session?.Position is not { } position
            || session.Duration is not { } duration)
        {
            return "--:-- / --:--";
        }

        return $"{FormatTime(position)} / {FormatTime(duration)}";
    }

    /// <summary>
    /// Formats a duration the way a music player displays it.
    /// </summary>
    /// <remarks>
    /// Hours appear only when there are any, so an ordinary track is not
    /// padded to <c>0:03:52</c>. Negative values are floored at zero because
    /// some players briefly report a negative position while seeking.
    /// </remarks>
    /// <param name="value">Duration to format.</param>
    /// <returns><c>m:ss</c>, or <c>h:mm:ss</c> for an hour or longer.</returns>
    /// <example>
    /// <code>
    /// FormatTime(TimeSpan.FromSeconds(232)); // "3:52"
    /// </code>
    /// </example>
    private static string FormatTime(TimeSpan value)
    {
        var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return clamped.TotalHours >= 1
            ? $"{(int)clamped.TotalHours}:{clamped.Minutes:00}:{clamped.Seconds:00}"
            : $"{(int)clamped.TotalMinutes}:{clamped.Seconds:00}";
    }

    /// <summary>
    /// Creates a text block in the panel's typographic style.
    /// </summary>
    /// <param name="size">Font size in device-independent pixels.</param>
    /// <param name="weight">Font weight.</param>
    /// <param name="brush">Foreground brush.</param>
    /// <param name="text">
    /// Static text, for labels that never change. Left null for blocks that
    /// <see cref="UpdateView"/> fills in.
    /// </param>
    /// <returns>The configured text block.</returns>
    private static TextBlock CreateTextBlock(
        double size,
        FontWeight weight,
        IBrush brush,
        string? text = null) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
        };

    /// <summary>
    /// Creates one of the three transport buttons.
    /// </summary>
    /// <remarks>
    /// Sized for a comfortable pointer target and left to stretch, so the
    /// three share the width of the panel evenly.
    /// </remarks>
    /// <param name="label">Button caption.</param>
    /// <returns>The configured button.</returns>
    private static Button CreateControlButton(string label) => new()
    {
        Content = label,
        Height = 46,
        FontSize = 14,
        FontWeight = FontWeight.SemiBold,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };
}
