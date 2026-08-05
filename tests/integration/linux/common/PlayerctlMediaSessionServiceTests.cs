using Reprise.Core;
using Reprise.Platform.Linux.Mpris;
using Xunit;

namespace Reprise.Platform.Linux.Tests;

public sealed class PlayerctlMediaSessionServiceTests
{
    [Fact]
    public async Task MetadataOutputBecomesLinuxMediaSessions()
    {
        var output = Record(
            "spotify",
            "Playing",
            "Bridge Song",
            "Bridge Artist",
            "Bridge Album",
            "180000000",
            "12000000",
            "0.64",
            "https://example.com/art.jpg");
        var runner = new RecordingProcessRunner(
            new ProcessResult(0, output, string.Empty));
        var service = new PlayerctlMediaSessionService(runner);

        var sessions = await service.GetSessionsAsync();

        var session = Assert.Single(sessions);
        Assert.Equal("spotify", session.PlayerId);
        Assert.Equal("Spotify", session.PlayerName);
        Assert.Equal(PlaybackStatus.Playing, session.Status);
        Assert.Equal("Bridge Song", session.Title);
        Assert.Equal(TimeSpan.FromSeconds(180), session.Duration);
        Assert.Equal(TimeSpan.FromSeconds(12), session.Position);
        Assert.Equal(0.64, session.Volume);
        Assert.Equal(new Uri("https://example.com/art.jpg"), session.ArtworkUri);
    }

    [Fact]
    public async Task ExitCodeOneMeansNoMprisPlayerIsRunning()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult(1, string.Empty, "No players found"));
        var service = new PlayerctlMediaSessionService(runner);

        var sessions = await service.GetSessionsAsync();

        Assert.Empty(sessions);
    }

    [Theory]
    [InlineData(PlaybackCommand.Previous, "previous")]
    [InlineData(PlaybackCommand.PlayPause, "play-pause")]
    [InlineData(PlaybackCommand.Next, "next")]
    public async Task PlaybackCommandsTargetTheSelectedPlayer(
        PlaybackCommand command,
        string expectedCommand)
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult(0, string.Empty, string.Empty));
        var service = new PlayerctlMediaSessionService(runner);

        await service.SendCommandAsync("firefox.instance42", command);

        Assert.Equal("playerctl", runner.FileName);
        Assert.Equal(
            ["--player", "firefox.instance42", expectedCommand],
            runner.Arguments);
    }

    [Fact]
    public async Task PlayerctlErrorsIncludeTheNativeDiagnostic()
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult(2, string.Empty, "Player does not support next"));
        var service = new PlayerctlMediaSessionService(runner);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendCommandAsync("vlc", PlaybackCommand.Next));

        Assert.Contains("Player does not support next", error.Message);
    }

    private static string Record(params string[] fields) =>
        string.Join(PlayerctlOutputParser.FieldSeparator, fields)
        + PlayerctlOutputParser.RecordSeparator;

    private sealed class RecordingProcessRunner(ProcessResult result)
        : IProcessRunner
    {
        public string? FileName { get; private set; }

        public IReadOnlyList<string>? Arguments { get; private set; }

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            FileName = fileName;
            Arguments = arguments;
            return Task.FromResult(result);
        }
    }
}
