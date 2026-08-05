namespace Reprise.Platform.Linux.Mpris;

public sealed class PlayerctlUnavailableException : InvalidOperationException
{
    public PlayerctlUnavailableException(Exception? innerException = null)
        : base(
            "playerctl이 설치되어 있지 않습니다. Ubuntu에서는 'sudo apt install playerctl'로 설치하세요.",
            innerException)
    {
    }
}
