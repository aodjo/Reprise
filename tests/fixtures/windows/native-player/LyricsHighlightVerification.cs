using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Reprise.Core;
using Reprise.Desktop;

/// <summary>Checks the actual click-opened player lyric renderer using isolated, explicitly test-only timed lines.</summary>
internal static class LyricsHighlightVerification
{
    /// <summary>Captures active, gap, and reopened lyric sheets through the production view model and player window.</summary>
    internal static async Task RunAsync(string root)
    {
        var backend = new DiagnosticMedia();
        var preferences = new PreferencesStore(Path.Combine(root, "lyrics-preferences.json"));
        preferences.Update(current => current with { PanelTheme = PanelTheme.White });
        using var model = new NowPlayingViewModel(backend, lyricsService: new DiagnosticLyrics(), preferences: () => preferences.Current);
        await model.RefreshAsync();
        var window = new NowPlayingWindow(model, preferences);
        try
        {
            window.ShowPanel();
            await Task.Delay(1000);
            var sheet = window.GetVisualDescendants().OfType<PanelLyricsView>().Single();
            var active = Capture(sheet, Path.Combine(root, "lyrics-active.png"));
            backend.Position = TimeSpan.FromSeconds(8);
            await model.RefreshAsync();
            await Task.Delay(800);
            var gap = Capture(sheet, Path.Combine(root, "lyrics-gap.png"));
            window.HidePanel();
            backend.Position = TimeSpan.FromSeconds(13);
            await model.RefreshAsync();
            window.ShowPanel();
            await Task.Delay(1000);
            var reopened = Capture(sheet, Path.Combine(root, "lyrics-reopened.png"));
            var ok = sheet.IsEffectivelyVisible && model.Lyrics is not null && active.Emphasized && gap.Emphasized && reopened.Emphasized;
            File.WriteAllText(Path.Combine(root, "lyrics-result.json"), JsonSerializer.Serialize(new
            {
                ok, active, gap, reopened,
                sheet.Bounds, sheet.IsEffectivelyVisible, state = model.LyricsState.ToString(),
                currentPosition = model.DisplayedPosition().TotalSeconds,
            }));
            if (!ok) Environment.ExitCode = 1;
        }
        finally { window.AllowClose = true; window.Close(); }
    }

    /// <summary>Reads only this diagnostic's lyric surface, preserving glyph alpha for measurable row contrast.</summary>
    private static RowContrast Capture(PanelLyricsView sheet, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)sheet.Bounds.Width, (int)sheet.Bounds.Height), new Vector(96, 96));
        bitmap.Render(sheet);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        return new(PeakAlpha(bitmap, 31), PeakAlpha(bitmap, 60));
    }

    /// <summary>Measures real glyph coverage within a row away from the viewport's edge fades.</summary>
    private static byte PeakAlpha(RenderTargetBitmap bitmap, int top)
    {
        var width = bitmap.PixelSize.Width;
        var bytes = new byte[width * 29 * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, top, width, 29), pinned.AddrOfPinnedObject(), bytes.Length, width * 4);
            byte peak = 0;
            for (var index = 3; index < bytes.Length; index += 4) peak = Math.Max(peak, bytes[index]);
            return peak;
        }
        finally { pinned.Free(); }
    }

    /// <summary>Measured current-row and neighbor alpha, rather than the requested animation state.</summary>
    private sealed record RowContrast(byte CurrentAlpha, byte NeighborAlpha)
    {
        /// <summary>Requires a fully visible current line and a clearly dimmed adjacent line.</summary>
        public bool Emphasized => CurrentAlpha >= 230 && NeighborAlpha is >= 70 and <= 110;
    }

    /// <summary>Identical words make current-row and neighboring-row glyph opacity directly comparable.</summary>
    private sealed class DiagnosticLyrics : ILyricsService
    {
        /// <inheritdoc />
        public Task<SyncedLyrics?> FetchSyncedLyricsAsync(LyricsTrackQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<SyncedLyrics?>(new SyncedLyrics(LyricsSource.Vibe,
                Enumerable.Range(0, 8).Select(index => new LyricLine(TimeSpan.FromSeconds(index * 5),
                    TimeSpan.FromSeconds(index * 5 + 2), "현재 가사 강조 확인")).ToArray()));
    }

    /// <summary>A paused diagnostic-only session controls time without changing an actual music player.</summary>
    private sealed class DiagnosticMedia : IMediaSessionService
    {
        internal TimeSpan Position { get; set; } = TimeSpan.FromSeconds(6);
        /// <inheritdoc />
        public Task<IReadOnlyList<MediaSessionSnapshot>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaSessionSnapshot>>([new("diagnostic", "Lyric renderer diagnostic", PlaybackStatus.Paused,
                "가사 강조 검사", "Reprise diagnostic", "Test-only timed lyrics", TimeSpan.FromSeconds(60), Position, null, null, DateTimeOffset.UtcNow)]);
        /// <inheritdoc />
        public Task SendCommandAsync(string playerId, PlaybackCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        /// <inheritdoc />
        public Task SeekAsync(string playerId, TimeSpan position, CancellationToken cancellationToken = default) { Position = position; return Task.CompletedTask; }
        /// <inheritdoc />
        public Task SetVolumeAsync(string playerId, double volume, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
