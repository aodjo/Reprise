using Reprise.Core;

namespace Reprise.Platform.Linux.Mpris;

public sealed class PlayerctlMediaSessionService : IMediaSessionService
{
    private static readonly string MetadataFormat = string.Join(
        PlayerctlOutputParser.FieldSeparator,
        "{{playerInstance}}",
        "{{status}}",
        "{{xesam:title}}",
        "{{xesam:artist}}",
        "{{xesam:album}}",
        "{{mpris:length}}",
        "{{position}}",
        "{{volume}}",
        $"{{{{mpris:artUrl}}}}{PlayerctlOutputParser.RecordSeparator}");

    private readonly IProcessRunner _processRunner;

    internal PlayerctlMediaSessionService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<MediaSessionSnapshot>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync(
            "playerctl",
            ["--all-players", "metadata", "--format", MetadataFormat],
            cancellationToken);

        if (result.ExitCode == 1)
        {
            return [];
        }

        if (result.ExitCode != 0)
        {
            throw CreateCommandException("metadata", result);
        }

        return PlayerctlOutputParser.Parse(
            result.StandardOutput,
            DateTimeOffset.UtcNow);
    }

    public async Task SendCommandAsync(
        string playerId,
        PlaybackCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var commandName = command switch
        {
            PlaybackCommand.Previous => "previous",
            PlaybackCommand.PlayPause => "play-pause",
            PlaybackCommand.Next => "next",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        var result = await _processRunner.RunAsync(
            "playerctl",
            ["--player", playerId, commandName],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw CreateCommandException(commandName, result);
        }
    }

    private static InvalidOperationException CreateCommandException(
        string command,
        ProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"exit code {result.ExitCode}"
            : result.StandardError.Trim();
        return new InvalidOperationException(
            $"playerctl {command} failed: {detail}");
    }
}
