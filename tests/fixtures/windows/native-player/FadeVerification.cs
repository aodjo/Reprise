using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Reprise.Desktop;

/// <summary>Verifies actual initial-frame glyph pixels without requiring a visible system taskbar.</summary>
internal static class FadeVerification
{
    /// <summary>Compares the leading-fade rendering to a completely unfaded sentence start.</summary>
    internal static async Task RunAsync(string root)
    {
        var line = new PanelTitleMarquee
        {
            Title = "첫 글자는 선명하게 보이고 스크롤로 잘려 나가는 글자만 흐려져야 합니다",
            FontSize = 13, FadesLeadingEdge = true,
        };
        TextOptions.SetTextRenderingMode(line, TextRenderingMode.Antialias);
        var window = new Window
        {
            Width = 200, Height = 48, MinWidth = 1, MinHeight = 1,
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false,
            Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = line, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        try
        {
            window.Show();
            await Task.Delay(150);
            line.RestartScroll();
            Capture(window, Path.Combine(root, "fade-start.png"));
            line.FadesLeadingEdge = false;
            Capture(window, Path.Combine(root, "fade-reference.png"));
            var sharp = File.ReadAllBytes(Path.Combine(root, "fade-start.png"))
                .AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "fade-reference.png")));
            line.FadesLeadingEdge = true;
            line.AutomaticallyScrolls = false;
            Capture(window, Path.Combine(root, "fade-disabled-scroll.png"));
            var stationary = File.ReadAllBytes(Path.Combine(root, "fade-disabled-scroll.png"))
                .AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "fade-reference.png")));
            File.WriteAllText(Path.Combine(root, "fade-result.json"), JsonSerializer.Serialize(new { ok = sharp && stationary, sharp, stationary }));
            if (!sharp || !stationary) Environment.ExitCode = 1;
        }
        finally { window.Close(); }
    }

    /// <summary>Captures a fresh transparent bitmap from this diagnostic window's own visual tree.</summary>
    private static void Capture(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 48), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
