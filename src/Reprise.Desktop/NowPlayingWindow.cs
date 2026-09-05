using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Reprise.Core;

namespace Reprise.Desktop;

public sealed class NowPlayingWindow : Window
{
    private static readonly IBrush MutedBrush = Brush.Parse("#99A3B5");
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

    public bool AllowClose { get; set; }

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

    private void HandleViewModelChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(UpdateView);
    }

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

    private static string FormatTimeline(MediaSessionSnapshot? session)
    {
        if (session?.Position is not { } position
            || session.Duration is not { } duration)
        {
            return "--:-- / --:--";
        }

        return $"{FormatTime(position)} / {FormatTime(duration)}";
    }

    private static string FormatTime(TimeSpan value)
    {
        var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return clamped.TotalHours >= 1
            ? $"{(int)clamped.TotalHours}:{clamped.Minutes:00}:{clamped.Seconds:00}"
            : $"{(int)clamped.TotalMinutes}:{clamped.Seconds:00}";
    }

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

    private static Button CreateControlButton(string label) => new()
    {
        Content = label,
        Height = 46,
        FontSize = 14,
        FontWeight = FontWeight.SemiBold,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };
}
