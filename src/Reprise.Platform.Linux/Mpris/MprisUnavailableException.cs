namespace Reprise.Platform.Linux.Mpris;

/// <summary>
/// Thrown when the D-Bus session bus itself cannot be reached.
/// </summary>
/// <remarks>
/// This is the one MPRIS failure that is not the user's music player acting
/// up: without a session bus there is nothing to query at all, which normally
/// means the process was launched outside a desktop session - over plain SSH,
/// from a system service, or in a container with no
/// <c>DBUS_SESSION_BUS_ADDRESS</c>. It is kept distinct from the per-player
/// errors so the UI can explain how to fix it instead of reporting that no
/// music is playing.
/// </remarks>
public sealed class MprisUnavailableException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception with the standard user-facing explanation.
    /// </summary>
    /// <remarks>
    /// The message is fixed rather than caller-supplied because the remedy is
    /// always the same, and it is written in Korean to match the rest of the
    /// desktop UI. The underlying transport error is preserved as the inner
    /// exception for logs.
    /// </remarks>
    /// <param name="innerException">
    /// The D-Bus connection failure that caused this, when one is available.
    /// Defaults to null.
    /// </param>
    /// <example>
    /// <code>
    /// throw new MprisUnavailableException(connectFailure);
    /// </code>
    /// </example>
    public MprisUnavailableException(Exception? innerException = null)
        : base(
            "D-Bus 세션 버스에 연결할 수 없습니다. 데스크톱 세션 안에서 실행하세요.",
            innerException)
    {
    }
}
