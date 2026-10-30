using System.Numerics;
using Reprise.Platform.Windows.Glass;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>Verifies real geometric displacement rather than an opacity-only material.</summary>
public sealed class GlassOpticsTests
{
    /// <summary>A flat center has no bevel displacement; opposite boundaries bend in opposite directions.</summary>
    [Fact]
    public void RefractionHasAFlatCenterAndSymmetricCurvedEdges()
    {
        var size = new Vector2(400, 240);
        Assert.Equal(Vector2.Zero, GlassOptics.Displacement(size / 2, size, 18));
        var left = GlassOptics.Displacement(new(1, 120), size, 18);
        var right = GlassOptics.Displacement(new(399, 120), size, 18);
        Assert.True(Math.Abs(left.X) > 1);
        Assert.Equal(-left.X, right.X, 4);
        Assert.Equal(0, left.Y);
        var top = GlassOptics.Displacement(new(200, 1), size, 18);
        Assert.Equal(left.X, top.Y, 4);
    }

    /// <summary>The lens samples different background pixels and gives the same physical result at twice the DPI.</summary>
    [Fact]
    public void NativeLensTransformsPixelsAndRespectsDpi()
    {
        var size = new Vector2(400, 240);
        var point = new Vector2(1, 120);
        var displacement = GlassOptics.Displacement(point, size, GlassOptics.Radius);
        var normal = GlassOptics.SourcePosition(point, size, displacement);
        Assert.NotEqual(point, normal);
        var scaled = GlassOptics.SourcePosition(point * 2, size * 2, displacement * 2);
        Assert.Equal(normal.X * 2, scaled.X, 4);
        Assert.Equal(normal.Y * 2, scaled.Y, 4);
        Assert.Equal(size / 2, GlassOptics.SourcePosition(size / 2, size, Vector2.Zero));
    }
}
