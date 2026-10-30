using Reprise.Platform.Windows.Media;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>
/// Queries the actual Windows broker on Windows runners without assuming a player is installed.
/// </summary>
public sealed class WindowsNativeTests
{
    /// <summary>
    /// Native activation and session enumeration must work for an unpackaged desktop application.
    /// </summary>
    [WindowsFact]
    public async Task EnumeratesRealWindowsMediaBroker()
    {
        var source = new WindowsMediaSessionSource();
        var sessions = await source.GetSessionsAsync(CancellationToken.None);
        Assert.NotNull(sessions);
        Assert.Equal(sessions.Count, sessions.Select(session => session.Id).Distinct().Count());
    }
}

/// <summary>
/// Marks native Windows checks explicitly skipped on the cross-compiling development host.
/// </summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    /// <summary>
    /// Requires a Windows host providing the media broker used by this integration check.
    /// </summary>
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Skip = "Requires the real Windows media broker.";
        }
    }
}
