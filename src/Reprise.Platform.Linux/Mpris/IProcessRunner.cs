namespace Reprise.Platform.Linux.Mpris;

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
