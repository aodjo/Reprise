using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Reprise.Desktop;
using Reprise.Platform.Windows.Media;
using Reprise.Platform.Windows.Glass;
using Avalonia.VisualTree;

/// <summary>Inspects the actual Windows backdrop granted to the player, using isolated preferences.</summary>
internal static class MaterialVerification
{
    /// <summary>Checks the live refractive material against an owned patterned window, including theme and lifetime transitions.</summary>
    internal static async Task RunAsync(string root)
    {
        var preferences = new PreferencesStore(Path.Combine(root, "material-preferences.json"));
        preferences.Update(current => current with { PanelTheme = PanelTheme.Liquid, AutomaticallyPausesOtherPlayer = false });
        using var model = new NowPlayingViewModel(new WindowsMediaSessionService(), preferences: () => preferences.Current);
        await model.RefreshAsync();
        var window = new NowPlayingWindow(model, preferences) { Topmost = true, ShowActivated = false };
        var opticalPattern = new OpticalPattern();
        var pattern = new Window
        {
            Width = 700, Height = 900, Position = new PixelPoint(150, 100),
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false,
            Content = opticalPattern, Topmost = true,
        };
        using var glass = new WindowsGlassBackdrop(window, preferences);
        try
        {
            pattern.Show();
            await Task.Delay(200);
            window.Show();
            await Task.Delay(500);
            window.Position = new PixelPoint(200, 150);
            await Task.Delay(200);
            var patternHandle = pattern.TryGetPlatformHandle()!.Handle;
            if (glass.BackgroundHandle != 0)
                GlassNativeWindow.Place(patternHandle, glass.BackgroundHandle, GlassNativeWindow.Bounds(patternHandle));
            await Task.Delay(200);
            var initial = window.ActualTransparencyLevel.ToString();
            var tintAlpha = ((SolidColorBrush)((Border)window.Content!).Background!).Color.A;
            var renderedAlpha = CaptureSurface(window, Path.Combine(root, "material-foreground.png"));
            var active = window.RefractiveBackdropActive;
            var regions = glass.RegionCount;
            var sources = glass.SourceCount;
            var whiteForeground = window.GetVisualDescendants().OfType<PanelTitleMarquee>()
                .All(title => title.Foreground is SolidColorBrush { Color: var color } && color == Colors.White)
                && window.GetVisualDescendants().OfType<PanelLyricsView>()
                    .All(lyrics => lyrics.Foreground is SolidColorBrush { Color: var color } && color == Colors.White);
            double? centerContrast = null;
            double? edgeContrast = null;
            double[]? beforeColor = null;
            double[]? afterColor = null;
            string? captureError = null;
            var liveContentChanged = false;
            if (active)
            {
                try
                {
                    await OwnWindowCapture.SaveAsync(glass.BackgroundHandle, Path.Combine(root, "material-native.png"));
                    centerContrast = StripeContrast(Path.Combine(root, "material-native.png"), edge: false);
                    edgeContrast = StripeContrast(Path.Combine(root, "material-native.png"), edge: true);
                    beforeColor = CaptureComposite(window, Path.Combine(root, "material-native.png"), Path.Combine(root, "material-composite-before.png"));
                    opticalPattern.Change();
                    await Task.Delay(700);
                    await OwnWindowCapture.SaveAsync(patternHandle, Path.Combine(root, "material-source-live.png"));
                    await OwnWindowCapture.SaveAsync(glass.BackgroundHandle, Path.Combine(root, "material-live.png"));
                    afterColor = CaptureComposite(window, Path.Combine(root, "material-live.png"), Path.Combine(root, "material-composite-after.png"));
                    liveContentChanged = !File.ReadAllBytes(Path.Combine(root, "material-native.png")).AsSpan()
                        .SequenceEqual(File.ReadAllBytes(Path.Combine(root, "material-live.png")));
                }
                catch (Exception error) { captureError = error.ToString(); }
            }
            preferences.Update(current => current with { PanelTheme = PanelTheme.White });
            var disabledForWhite = !window.RefractiveBackdropActive;
            preferences.Update(current => current with { PanelTheme = PanelTheme.Liquid });
            await Task.Delay(500);
            window.Hide();
            var hiddenStopped = !window.RefractiveBackdropActive && glass.SourceCount == 0;
            window.Show();
            await Task.Delay(200);
            var compositeDifference = beforeColor is not null && afterColor is not null
                ? beforeColor.Zip(afterColor, (a, b) => Math.Abs(a - b)).Average() : 0;
            var compositeChroma = beforeColor is not null ? beforeColor.Max() - beforeColor.Min() : 0;
            // Normalize the color response by measured foreground transmission so a darker tint is not mistaken for a dead lens.
            var colorResponse = compositeDifference / Math.Max(0.01, (255 - renderedAlpha) / 255.0);
            var colorTransmitted = initial == nameof(WindowTransparencyLevel.Transparent) && colorResponse > 24 && compositeChroma > 10;
            var ok = active && window.RefractiveBackdropActive && disabledForWhite && hiddenStopped && liveContentChanged && regions > 1
                && tintAlpha >= 120 && renderedAlpha >= 120 && colorTransmitted && whiteForeground
                && centerContrast is < 35 && edgeContrast is < 40;
            File.WriteAllText(Path.Combine(root, "material-result.json"), JsonSerializer.Serialize(new
            {
                ok,
                renderer = "DWM live lens", initial, active, regions, sources, disabledForWhite, window.IsVisible, glass.Error, captureError,
                liveContentChanged, hiddenStopped,
                tintAlpha, renderedAlpha, nativeHostBlur = initial == nameof(WindowTransparencyLevel.AcrylicBlur),
                scatteringKernel = regions >= 208 ? "128-point Gaussian" : "none",
                whiteForeground, centerContrast, edgeContrast,
                beforeColor, afterColor, compositeDifference, colorResponse, compositeChroma, colorTransmitted,
                patternPosition = pattern.Position.ToString(), playerPosition = window.Position.ToString(), patternSize = pattern.Bounds.ToString(),
            }));
            if (!ok || glass.Error is not null) Environment.ExitCode = 1;
        }
        finally { window.AllowClose = true; window.Close(); pattern.Close(); }
    }

    /// <summary>Measures residual stripe contrast in the native lens center or side bevel, rejecting a sharp border around a frosted center.</summary>
    private static double StripeContrast(string path, bool edge)
    {
        using var bitmap = new Bitmap(path);
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var bytes = new byte[width * height * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), bytes.Length, width * 4);
            var left = edge ? 4 : 40;
            var right = edge ? 14 : width - 40;
            double sum = 0, squared = 0;
            var count = 0;
            for (var y = 40; y < height - 40; y++)
            for (var x = left; x < right; x++)
            {
                var red = bytes[(y * width + x) * 4 + 2];
                sum += red;
                squared += red * red;
                count++;
            }
            return Math.Sqrt(Math.Max(0, squared / count - Math.Pow(sum / count, 2)));
        }
        finally { pinned.Free(); }
    }

    /// <summary>Composes only the diagnostic-owned live lens and actual foreground, whose transparent-window contract permits ordinary source-over composition.</summary>
    private static double[] CaptureComposite(Window window, string backgroundPath, string outputPath)
    {
        using var backdrop = new Bitmap(backgroundPath);
        var size = window.Bounds.Size;
        using var foreground = new RenderTargetBitmap(backdrop.PixelSize, new Vector(96 * window.RenderScaling, 96 * window.RenderScaling));
        foreground.Render(window);
        var image = new Grid
        {
            Width = size.Width, Height = size.Height,
            Children =
            {
                new Image { Source = backdrop, Stretch = Stretch.Fill },
                new Image { Source = foreground, Stretch = Stretch.Fill },
            },
        };
        image.Measure(size);
        image.Arrange(new Rect(size));
        using var composite = new RenderTargetBitmap(backdrop.PixelSize, new Vector(96 * window.RenderScaling, 96 * window.RenderScaling));
        composite.Render(image);
        composite.Save(outputPath, PngBitmapEncoderOptions.Default);
        var bytes = new byte[composite.PixelSize.Width * composite.PixelSize.Height * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            composite.CopyPixels(new PixelRect(composite.PixelSize), pinned.AddrOfPinnedObject(), bytes.Length, composite.PixelSize.Width * 4);
            double[] sums = [0, 0, 0];
            for (var index = 0; index < bytes.Length; index += 4)
                for (var channel = 0; channel < 3; channel++) sums[channel] += bytes[index + channel];
            return sums.Select(sum => sum / (bytes.Length / 4)).ToArray();
        }
        finally { pinned.Free(); }
    }

    /// <summary>Measures actual painted alpha in the empty side gutter, rather than inferring readability from a requested tint.</summary>
    private static byte CaptureSurface(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        var pixel = new byte[4];
        var pinned = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(6, 60, 1, 1), pinned.AddrOfPinnedObject(), pixel.Length, 4);
            return pixel[3];
        }
        finally { pinned.Free(); }
    }

    /// <summary>Known source stripes reveal actual native displacement without reading any unrelated application's pixels.</summary>
    private sealed class OpticalPattern : Control
    {
        private IBrush _stripe = Brushes.DodgerBlue;
        /// <summary>Changes the actual source window without rebuilding the lens.</summary>
        internal void Change() { _stripe = Brushes.LimeGreen; InvalidateVisual(); }
        /// <summary>Draws sharp alternating stripes and horizontal lines behind the diagnostic lens.</summary>
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
            for (var x = 0; x < Bounds.Width; x += 32)
                context.FillRectangle(_stripe, new Rect(x, 0, 16, Bounds.Height));
            for (var y = 0; y < Bounds.Height; y += 32)
                context.FillRectangle(Brushes.OrangeRed, new Rect(0, y, Bounds.Width, 2));
        }
    }
}
