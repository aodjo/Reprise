using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>
/// Vector glyphs the panel draws, in place of the SF Symbols macOS uses.
/// </summary>
/// <remarks>
/// Linux has no system symbol font, and shipping an icon font for a dozen
/// glyphs would be heavier than the glyphs themselves. Each shape is drawn
/// in a 24-unit box and scaled at the point of use, so one definition serves
/// the 11pt footer buttons and the 25pt transport controls alike. Filled
/// shapes are used throughout - outlines are built as rings - so every icon
/// renders through the same geometry fill.
/// </remarks>
public static class PanelIcons
{
    /// <summary>
    /// Outlined painter's palette with four wells, standing in for <c>paintpalette</c>.
    /// </summary>
    public static readonly Geometry Palette = Geometry.Parse(
        "F0 M2 12 A10 10 0 1 1 22 12 A10 10 0 1 1 2 12 Z M3.8 12 A8.2 8.2 0 1 1 20.2 12 A8.2 8.2 0 1 1 3.8 12 Z M5.8 9.5 A1.7 1.7 0 1 1 9.2 9.5 A1.7 1.7 0 1 1 5.8 9.5 Z M9.8 6.5 A1.7 1.7 0 1 1 13.2 6.5 A1.7 1.7 0 1 1 9.8 6.5 Z M14.8 8.5 A1.7 1.7 0 1 1 18.2 8.5 A1.7 1.7 0 1 1 14.8 8.5 Z M15.3 14 A1.7 1.7 0 1 1 18.7 14 A1.7 1.7 0 1 1 15.3 14 Z M12 20.5 L15 20.5 A2 2 0 0 0 15 16.5 L12 16.5 Z");

    /// <summary>
    /// Outlined window with a filled title strip, standing in for <c>menubar.rectangle</c>.
    /// </summary>
    public static readonly Geometry MenuBarRectangle = Geometry.Parse(
        "F0 M5 4.5 L19 4.5 A2.5 2.5 0 0 1 21.5 7 L21.5 17 A2.5 2.5 0 0 1 19 19.5 L5 19.5 A2.5 2.5 0 0 1 2.5 17 L2.5 7 A2.5 2.5 0 0 1 5 4.5 Z M5.5 6.3 L18.5 6.3 A1.2 1.2 0 0 1 19.7 7.5 L19.7 16.5 A1.2 1.2 0 0 1 18.5 17.7 L5.5 17.7 A1.2 1.2 0 0 1 4.3 16.5 L4.3 7.5 A1.2 1.2 0 0 1 5.5 6.3 Z M4.3 6.3 L19.7 6.3 L19.7 9.5 L4.3 9.5 Z");

    /// <summary>
    /// Outlined rounded square with an i cut out, standing in for <c>info.square</c>.
    /// </summary>
    public static readonly Geometry InfoSquare = Geometry.Parse(
        "F0 M7 3 L17 3 A4 4 0 0 1 21 7 L21 17 A4 4 0 0 1 17 21 L7 21 A4 4 0 0 1 3 17 L3 7 A4 4 0 0 1 7 3 Z M7.3 4.8 L16.7 4.8 A2.5 2.5 0 0 1 19.2 7.3 L19.2 16.7 A2.5 2.5 0 0 1 16.7 19.2 L7.3 19.2 A2.5 2.5 0 0 1 4.8 16.7 L4.8 7.3 A2.5 2.5 0 0 1 7.3 4.8 Z M11 10.5 L13 10.5 L13 17 L11 17 Z M10.8 7.9 A1.2 1.2 0 1 1 13.2 7.9 A1.2 1.2 0 1 1 10.8 7.9 Z");

    /// <summary>
    /// Ring with a play triangle cut out, standing in for <c>play.circle</c>.
    /// </summary>
    public static readonly Geometry PlayCircle = Geometry.Parse(
        "F0 M2 12 A10 10 0 1 1 22 12 A10 10 0 1 1 2 12 Z M3.8 12 A8.2 8.2 0 1 1 20.2 12 A8.2 8.2 0 1 1 3.8 12 Z M9.8 8.2 L16.2 12 L9.8 15.8 Z");

    /// <summary>
    /// Solid play triangle, standing in for SF Symbols <c>play.fill</c>.
    /// </summary>
    public static readonly Geometry Play = Geometry.Parse(
        "M6.5 3.5 L20.5 12 L6.5 20.5 Z");

    /// <summary>
    /// Two solid bars, standing in for <c>pause.fill</c>.
    /// </summary>
    public static readonly Geometry Pause = Geometry.Parse(
        "M5.5 4 L10 4 L10 20 L5.5 20 Z M14 4 L18.5 4 L18.5 20 L14 20 Z");

    /// <summary>
    /// Two solid left-pointing triangles, standing in for <c>backward.fill</c>.
    /// </summary>
    public static readonly Geometry Backward = Geometry.Parse(
        "M22 5 L12.5 12 L22 19 Z M12 5 L2.5 12 L12 19 Z");

    /// <summary>
    /// Two solid right-pointing triangles, standing in for <c>forward.fill</c>.
    /// </summary>
    public static readonly Geometry Forward = Geometry.Parse(
        "M2 5 L11.5 12 L2 19 Z M12 5 L21.5 12 L12 19 Z");

    /// <summary>
    /// Outlined eight-tooth gear, standing in for <c>gearshape</c>.
    /// </summary>
    public static readonly Geometry Gear = Geometry.Parse(
        "F0 M13.08 1.05 L15.19 1.47 L16.2 4.15 L17.65 5.12 L20.5 5.02 L21.7 6.81 L20.52 9.42 L20.86 11.13 L22.95 13.08 L22.53 15.19 L19.85 16.2 L18.88 17.65 L18.98 20.5 L17.19 21.7 L14.58 20.52 L12.87 20.86 L10.92 22.95 L8.81 22.53 L7.8 19.85 L6.35 18.88 L3.5 18.98 L2.3 17.19 L3.48 14.58 L3.14 12.87 L1.05 10.92 L1.47 8.81 L4.15 7.8 L5.12 6.35 L5.02 3.5 L6.81 2.3 L9.42 3.48 L11.13 3.14 Z M12.9 2.84 L14.67 3.2 L15.35 5.74 L16.5 6.51 L19.11 6.16 L20.11 7.66 L18.79 9.94 L19.07 11.3 L21.16 12.9 L20.8 14.67 L18.26 15.35 L17.49 16.5 L17.84 19.11 L16.34 20.11 L14.06 18.79 L12.7 19.07 L11.1 21.16 L9.33 20.8 L8.65 18.26 L7.5 17.49 L4.89 17.84 L3.89 16.34 L5.21 14.06 L4.93 12.7 L2.84 11.1 L3.2 9.33 L5.74 8.65 L6.51 7.5 L6.16 4.89 L7.66 3.89 L9.94 5.21 L11.3 4.93 Z M7.6 12 A4.4 4.4 0 1 1 16.4 12 A4.4 4.4 0 1 1 7.6 12 Z M9.4 12 A2.6 2.6 0 1 1 14.6 12 A2.6 2.6 0 1 1 9.4 12 Z");

    /// <summary>
    /// Open portrait frame with an arrow leaving it, standing in for <c>rectangle.portrait.and.arrow.right</c>.
    /// </summary>
    public static readonly Geometry Exit = Geometry.Parse(
        "M4 3 L14.5 3 L14.5 5.4 L6.4 5.4 L6.4 18.6 L14.5 18.6 L14.5 21 L4 21 Z M10.5 10.9 L17 10.9 L17 13.1 L10.5 13.1 Z M16.2 7.6 L21.6 12 L16.2 16.4 Z");

    /// <summary>
    /// Speaker with a cross beside it, standing in for <c>speaker.slash.fill</c>.
    /// </summary>
    public static readonly Geometry SpeakerMuted = Geometry.Parse(
        "M3 9 L7 9 L12.5 4.5 L12.5 19.5 L7 15 L3 15 Z M14.29 9.71 L20.29 15.71 L21.71 14.29 L15.71 8.29 Z M20.29 8.29 L14.29 14.29 L15.71 15.71 L21.71 9.71 Z");

    /// <summary>
    /// Speaker with one sound wave, standing in for <c>speaker.wave.1.fill</c>.
    /// </summary>
    public static readonly Geometry SpeakerWave1 = Geometry.Parse(
        "M3 9 L7 9 L12.5 4.5 L12.5 19.5 L7 15 L3 15 Z M15.36 8.52 L15.57 8.76 L15.76 9.02 L15.93 9.28 L16.09 9.56 L16.23 9.84 L16.35 10.14 L16.46 10.44 L16.55 10.74 L16.61 11.05 L16.66 11.37 L16.69 11.68 L16.7 12 L16.69 12.32 L16.66 12.63 L16.61 12.95 L16.55 13.26 L16.46 13.56 L16.35 13.86 L16.23 14.16 L16.09 14.44 L15.93 14.72 L15.76 14.98 L15.57 15.24 L15.36 15.48 L14.03 14.28 L14.16 14.12 L14.29 13.95 L14.4 13.78 L14.5 13.6 L14.59 13.41 L14.67 13.22 L14.74 13.02 L14.8 12.82 L14.84 12.62 L14.87 12.41 L14.89 12.21 L14.9 12 L14.89 11.79 L14.87 11.59 L14.84 11.38 L14.8 11.18 L14.74 10.98 L14.67 10.78 L14.59 10.59 L14.5 10.4 L14.4 10.22 L14.29 10.05 L14.16 9.88 L14.03 9.72 Z");

    /// <summary>
    /// Speaker with two sound waves, standing in for <c>speaker.wave.2.fill</c>.
    /// </summary>
    public static readonly Geometry SpeakerWave2 = Geometry.Parse(
        "M3 9 L7 9 L12.5 4.5 L12.5 19.5 L7 15 L3 15 Z M15.36 8.52 L15.57 8.76 L15.76 9.02 L15.93 9.28 L16.09 9.56 L16.23 9.84 L16.35 10.14 L16.46 10.44 L16.55 10.74 L16.61 11.05 L16.66 11.37 L16.69 11.68 L16.7 12 L16.69 12.32 L16.66 12.63 L16.61 12.95 L16.55 13.26 L16.46 13.56 L16.35 13.86 L16.23 14.16 L16.09 14.44 L15.93 14.72 L15.76 14.98 L15.57 15.24 L15.36 15.48 L14.03 14.28 L14.16 14.12 L14.29 13.95 L14.4 13.78 L14.5 13.6 L14.59 13.41 L14.67 13.22 L14.74 13.02 L14.8 12.82 L14.84 12.62 L14.87 12.41 L14.89 12.21 L14.9 12 L14.89 11.79 L14.87 11.59 L14.84 11.38 L14.8 11.18 L14.74 10.98 L14.67 10.78 L14.59 10.59 L14.5 10.4 L14.4 10.22 L14.29 10.05 L14.16 9.88 L14.03 9.72 Z M17.54 6.16 L17.9 6.56 L18.24 6.98 L18.54 7.43 L18.82 7.88 L19.07 8.36 L19.29 8.85 L19.47 9.36 L19.63 9.87 L19.75 10.4 L19.83 10.93 L19.88 11.46 L19.9 12 L19.88 12.54 L19.83 13.07 L19.75 13.6 L19.63 14.13 L19.47 14.64 L19.29 15.15 L19.07 15.64 L18.82 16.12 L18.54 16.57 L18.24 17.02 L17.9 17.44 L17.54 17.84 L16.25 16.58 L16.53 16.27 L16.79 15.94 L17.04 15.59 L17.25 15.23 L17.45 14.86 L17.62 14.47 L17.77 14.08 L17.88 13.67 L17.98 13.26 L18.05 12.84 L18.09 12.42 L18.1 12 L18.09 11.58 L18.05 11.16 L17.98 10.74 L17.88 10.33 L17.77 9.92 L17.62 9.53 L17.45 9.14 L17.25 8.77 L17.04 8.41 L16.79 8.06 L16.53 7.73 L16.25 7.42 Z");

    /// <summary>
    /// Speaker with three sound waves, standing in for <c>speaker.wave.3.fill</c>.
    /// </summary>
    public static readonly Geometry SpeakerWave3 = Geometry.Parse(
        "M3 9 L7 9 L12.5 4.5 L12.5 19.5 L7 15 L3 15 Z M15.36 8.52 L15.57 8.76 L15.76 9.02 L15.93 9.28 L16.09 9.56 L16.23 9.84 L16.35 10.14 L16.46 10.44 L16.55 10.74 L16.61 11.05 L16.66 11.37 L16.69 11.68 L16.7 12 L16.69 12.32 L16.66 12.63 L16.61 12.95 L16.55 13.26 L16.46 13.56 L16.35 13.86 L16.23 14.16 L16.09 14.44 L15.93 14.72 L15.76 14.98 L15.57 15.24 L15.36 15.48 L14.03 14.28 L14.16 14.12 L14.29 13.95 L14.4 13.78 L14.5 13.6 L14.59 13.41 L14.67 13.22 L14.74 13.02 L14.8 12.82 L14.84 12.62 L14.87 12.41 L14.89 12.21 L14.9 12 L14.89 11.79 L14.87 11.59 L14.84 11.38 L14.8 11.18 L14.74 10.98 L14.67 10.78 L14.59 10.59 L14.5 10.4 L14.4 10.22 L14.29 10.05 L14.16 9.88 L14.03 9.72 Z M17.54 6.16 L17.9 6.56 L18.24 6.98 L18.54 7.43 L18.82 7.88 L19.07 8.36 L19.29 8.85 L19.47 9.36 L19.63 9.87 L19.75 10.4 L19.83 10.93 L19.88 11.46 L19.9 12 L19.88 12.54 L19.83 13.07 L19.75 13.6 L19.63 14.13 L19.47 14.64 L19.29 15.15 L19.07 15.64 L18.82 16.12 L18.54 16.57 L18.24 17.02 L17.9 17.44 L17.54 17.84 L16.25 16.58 L16.53 16.27 L16.79 15.94 L17.04 15.59 L17.25 15.23 L17.45 14.86 L17.62 14.47 L17.77 14.08 L17.88 13.67 L17.98 13.26 L18.05 12.84 L18.09 12.42 L18.1 12 L18.09 11.58 L18.05 11.16 L17.98 10.74 L17.88 10.33 L17.77 9.92 L17.62 9.53 L17.45 9.14 L17.25 8.77 L17.04 8.41 L16.79 8.06 L16.53 7.73 L16.25 7.42 Z M19.56 3.66 L20.1 4.21 L20.6 4.81 L21.06 5.43 L21.48 6.08 L21.85 6.76 L22.18 7.47 L22.46 8.19 L22.69 8.93 L22.87 9.69 L23 10.45 L23.07 11.22 L23.1 12 L23.07 12.78 L23 13.55 L22.87 14.31 L22.69 15.07 L22.46 15.81 L22.18 16.53 L21.85 17.24 L21.48 17.92 L21.06 18.57 L20.6 19.19 L20.1 19.79 L19.56 20.34 L18.31 19.05 L18.76 18.58 L19.19 18.08 L19.58 17.55 L19.93 17 L20.24 16.42 L20.52 15.83 L20.76 15.22 L20.95 14.59 L21.1 13.95 L21.21 13.31 L21.28 12.66 L21.3 12 L21.28 11.34 L21.21 10.69 L21.1 10.05 L20.95 9.41 L20.76 8.78 L20.52 8.17 L20.24 7.58 L19.93 7 L19.58 6.45 L19.19 5.92 L18.76 5.42 L18.31 4.95 Z");

    /// <summary>
    /// Solid triangle with a cut-out exclamation mark, standing in for <c>exclamationmark.triangle.fill</c>.
    /// </summary>
    public static readonly Geometry Warning = Geometry.Parse(
        "F0 M12 2.5 L22.5 20.5 L1.5 20.5 Z M11 9 L13 9 L13 14.6 L11 14.6 Z M11 15.9 L13 15.9 L13 17.9 L11 17.9 Z");

    /// <summary>
    /// Single eighth note, standing in for <c>music.note</c>.
    /// </summary>
    public static readonly Geometry MusicNote = Geometry.Parse(
        "M10.5 3.5 L12.6 3.5 C16.2 4.6 18.4 6.8 18.4 10.6 C16.8 8.6 14.8 8 12.6 7.9 L12.6 17.2 C12.6 19.4 10.6 21 8.3 21 C6.2 21 4.6 19.8 4.6 18.2 C4.6 16.4 6.6 15 8.9 15 C9.5 15 10 15.1 10.5 15.3 Z");

    /// <summary>
    /// Broken ring with a vertical bar, standing in for <c>power</c>.
    /// </summary>
    public static readonly Geometry Power = Geometry.Parse(
        "M16.35 4.97 L17.88 6.09 L19.13 7.51 L20.04 9.17 L20.57 10.99 L20.69 12.88 L20.4 14.75 L19.72 16.52 L18.66 18.09 L17.3 19.4 L15.68 20.38 L13.88 20.99 L12 21.2 L10.12 20.99 L8.32 20.38 L6.7 19.4 L5.34 18.09 L4.28 16.52 L3.6 14.75 L3.31 12.88 L3.43 10.99 L3.96 9.17 L4.87 7.51 L6.12 6.09 L7.65 4.97 L8.65 6.7 L7.47 7.56 L6.51 8.66 L5.81 9.94 L5.4 11.34 L5.31 12.79 L5.53 14.23 L6.06 15.59 L6.87 16.81 L7.92 17.82 L9.17 18.57 L10.55 19.04 L12 19.2 L13.45 19.04 L14.83 18.57 L16.08 17.82 L17.13 16.81 L17.94 15.59 L18.47 14.23 L18.69 12.79 L18.6 11.34 L18.19 9.94 L17.49 8.66 L16.53 7.56 L15.35 6.7 Z M11 2.5 L13 2.5 L13 11.5 L11 11.5 Z");

    /// <summary>
    /// Level bars inside a disc, standing in for <c>waveform.circle.fill</c>; the Spotify artwork placeholder.
    /// </summary>
    public static readonly Geometry WaveformCircle = Geometry.Parse(
        "F0 M1 12 A11 11 0 1 1 23 12 A11 11 0 1 1 1 12 Z M5.5 9.5 L7.5 9.5 L7.5 14.5 L5.5 14.5 Z M8.5 7 L10.5 7 L10.5 17 L8.5 17 Z M11.5 5 L13.5 5 L13.5 19 L11.5 19 Z M14.5 8 L16.5 8 L16.5 16 L14.5 16 Z M17.5 10 L19.5 10 L19.5 14 L17.5 14 Z");

    /// <summary>
    /// Play triangle cut out of a rectangle, standing in for <c>play.rectangle.fill</c>; the YouTube Music artwork placeholder.
    /// </summary>
    public static readonly Geometry PlayRectangle = Geometry.Parse(
        "F0 M2 5 L22 5 L22 19 L2 19 Z M9.5 8.5 L16.5 12 L9.5 15.5 Z");

    /// <summary>
    /// Spotify mark, converted from the vector asset the macOS app ships.
    /// </summary>
    public static readonly Geometry SpotifyLogo = Geometry.Parse(
        "M122.37 3.31 C61.99 0.91 11.1 47.91 8.71 108.29 C6.31 168.67 53.32 219.55 113.69 221.95 C174.07 224.35 224.95 177.35 227.35 116.97 C229.74 56.59 182.74 5.7 122.37 3.31 Z M168.55 163.59 C167.19 165.99 164.54 167.19 161.96 166.83 C161.17 166.72 160.38 166.46 159.64 166.04 C145.18 157.81 129.42 152.45 112.8 150.11 C96.18 147.77 79.55 148.58 63.38 152.51 C59.87 153.36 56.34 151.21 55.49 147.7 C54.64 144.19 56.79 140.66 60.3 139.81 C78.08 135.49 96.36 134.6 114.62 137.17 C132.88 139.74 150.2 145.63 166.11 154.68 C169.24 156.47 170.34 160.45 168.56 163.59 Z M182.93 134.87 C180.7 138.99 175.54 140.53 171.42 138.3 C154.5 129.15 136.18 123.14 116.97 120.44 C97.76 117.74 78.5 118.47 59.71 122.6 C58.69 122.82 57.68 122.86 56.7 122.72 C53.29 122.24 50.37 119.7 49.59 116.13 C48.58 111.55 51.48 107.02 56.06 106.01 C76.83 101.44 98.12 100.63 119.34 103.61 C140.55 106.59 160.8 113.23 179.5 123.35 C183.63 125.58 185.16 130.73 182.93 134.86 Z M198.87 102.49 C196.77 106.53 192.4 108.62 188.14 108.02 C186.99 107.86 185.86 107.5 184.77 106.94 C165.07 96.69 143.85 89.92 121.7 86.81 C99.55 83.7 77.28 84.36 55.52 88.78 C49.86 89.93 44.35 86.27 43.2 80.62 C42.05 74.96 45.71 69.45 51.36 68.3 C75.46 63.41 100.1 62.68 124.61 66.12 C149.12 69.56 172.6 77.06 194.42 88.41 C199.54 91.07 201.53 97.38 198.87 102.5 Z");

    /// <summary>
    /// YouTube Music mark, converted from the vector asset the macOS app ships.
    /// </summary>
    public static readonly Geometry YouTubeMusicLogo = Geometry.Parse(
        "F0 M1 12 A11 11 0 1 1 23 12 A11 11 0 1 1 1 12 Z M5.25 12 A6.75 6.75 0 1 0 18.75 12 A6.75 6.75 0 1 0 5.25 12 Z M9.75 8.25 L16.25 12 L9.75 15.75 Z");

    /// <summary>
    /// Picks the speaker glyph that matches a volume level.
    /// </summary>
    /// <remarks>
    /// Thresholds copy the macOS panel, so the two platforms flip between
    /// wave counts at the same levels.
    /// </remarks>
    /// <param name="percent">Volume from 0 to 100.</param>
    /// <returns>The glyph for that level.</returns>
    /// <example>
    /// <code>
    /// PanelIcons.Speaker(50); // PanelIcons.SpeakerWave2
    /// </code>
    /// </example>
    public static Geometry Speaker(int percent) => percent switch
    {
        <= 0 => SpeakerMuted,
        < 34 => SpeakerWave1,
        < 67 => SpeakerWave2,
        _ => SpeakerWave3,
    };
}
