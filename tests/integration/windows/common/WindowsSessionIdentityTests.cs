using Reprise.Platform.Windows.Media;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>Regression coverage for native wrapper recreation observed on real Windows.</summary>
public sealed class WindowsSessionIdentityTests
{
    /// <summary>A unique player retains its ID even when the broker recreates the COM object.</summary>
    [Fact]
    public void RecreatedUniquePlayerKeepsIdentity()
    {
        WindowsSessionIdentity[] old = [new("Spotify", 1), new("Chrome", 2)];
        WindowsSessionIdentity[] current = [new("Chrome", 10), new("Spotify", 11)];
        Assert.Equal(1, WindowsSessionIdentity.Match(old, current, 0));
        Assert.Equal(0, WindowsSessionIdentity.Match(old, current, 1));
    }

    /// <summary>Duplicate sessions can be reconciled safely when their native objects persist.</summary>
    [Fact]
    public void NativeIdentityDistinguishesSameAppSessions()
    {
        WindowsSessionIdentity[] old = [new("Chrome", 1), new("Chrome", 2)];
        WindowsSessionIdentity[] current = [new("Chrome", 2), new("Chrome", 1)];
        Assert.Equal(1, WindowsSessionIdentity.Match(old, current, 0));
        Assert.Equal(0, WindowsSessionIdentity.Match(old, current, 1));
    }

    /// <summary>Ambiguous recreation or removal never routes a stale ID to a different session.</summary>
    [Fact]
    public void AmbiguousSameAppSessionsAreNeverGuessed()
    {
        WindowsSessionIdentity[] old = [new("Chrome", 1), new("Chrome", 2)];
        WindowsSessionIdentity[] recreated = [new("Chrome", 10), new("Chrome", 20)];
        Assert.Equal(-1, WindowsSessionIdentity.Match(old, recreated, 0));
        Assert.Equal(-1, WindowsSessionIdentity.Match(old, recreated, 1));
        Assert.Equal(-1, WindowsSessionIdentity.Match(old, [new("Chrome", 30)], 0));
        Assert.Equal(-1, WindowsSessionIdentity.Match([new("Chrome", 30)], recreated, 0));
    }
}
