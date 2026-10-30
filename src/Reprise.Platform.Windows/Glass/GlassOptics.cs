using System.Numerics;

namespace Reprise.Platform.Windows.Glass;

/// <summary>Rounded glass bevel optics, evaluated independently of native rendering.</summary>
internal static class GlassOptics
{
    /// <summary>Index of refraction of the simulated clear glass.</summary>
    internal const float RefractiveIndex = 1.45f;
    /// <summary>Thickness of the curved edge in device-independent pixels.</summary>
    internal const float Bevel = 16;
    /// <summary>Corner radius shared with the foreground panel.</summary>
    internal const float Radius = (float)Reprise.Desktop.NowPlayingWindow.PanelCornerRadius;

    /// <summary>Computes a background displacement from Snell's law and the rounded edge normal.</summary>
    internal static Vector2 Displacement(Vector2 point, Vector2 size, float radius)
    {
        var center = size / 2;
        var q = Vector2.Abs(point - center) - (center - new Vector2(radius));
        var outside = Vector2.Max(q, Vector2.Zero);
        var distance = outside.Length() + Math.Min(Math.Max(q.X, q.Y), 0) - radius;
        var edge = Math.Clamp(1 + distance / Bevel, 0, 1);
        if (edge <= 0) return Vector2.Zero;
        Vector2 normal;
        if (outside.LengthSquared() > 0) normal = Vector2.Normalize(outside);
        else normal = q.X > q.Y ? Vector2.UnitX : Vector2.UnitY;
        normal *= new Vector2(point.X < center.X ? -1 : 1, point.Y < center.Y ? -1 : 1);
        var slope = 0.9f * edge * edge;
        var nz = MathF.Sqrt(1 - slope * slope);
        var eta = 1 / RefractiveIndex;
        var k = MathF.Sqrt(1 - eta * eta * (1 - nz * nz));
        var factor = k - eta * nz;
        var rayZ = eta + factor * nz;
        return -normal * (factor * slope / rayZ) * 14;
    }

    /// <summary>Maps a region's screen coordinate back through the magnified lens to its actual backdrop source.</summary>
    internal static Vector2 SourcePosition(Vector2 point, Vector2 size, Vector2 displacement) =>
        (point - size / 2 + displacement) / GlassScene.Magnification + size / 2;
}
