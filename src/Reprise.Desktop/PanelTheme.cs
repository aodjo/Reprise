namespace Reprise.Desktop;

/// <summary>
/// Visual treatment of the player panel, mirroring the macOS choices.
/// </summary>
/// <remarks>
/// The raw values are the strings the macOS app stores, so a preference
/// file can be read the same way on every platform.
/// </remarks>
public enum PanelTheme
{
    /// <summary>
    /// Opaque white with dark text, regardless of the desktop theme.
    /// </summary>
    White,

    /// <summary>
    /// Opaque near-black with light text, regardless of the desktop theme.
    /// </summary>
    Dark,

    /// <summary>
    /// Translucent panel that follows the desktop's light or dark setting.
    /// </summary>
    Liquid,

    /// <summary>
    /// Opaque panel in the desktop's own window colour and text colour.
    /// </summary>
    System,
}

/// <summary>
/// Which value the left-hand time label under the scrubber shows.
/// </summary>
public enum PanelLeadingTimeStyle
{
    /// <summary>
    /// Time elapsed in the current track.
    /// </summary>
    Elapsed,

    /// <summary>
    /// A fixed <c>00:00</c>, for users who prefer a static left edge.
    /// </summary>
    Zero,
}

/// <summary>
/// Which value the right-hand time label under the scrubber shows.
/// </summary>
public enum PanelTrailingTimeStyle
{
    /// <summary>
    /// Time left in the current track, shown with a leading minus sign.
    /// </summary>
    Remaining,

    /// <summary>
    /// Total length of the current track.
    /// </summary>
    Duration,
}

/// <summary>
/// What text the tray label shows beside the icon, mirroring the macOS menu
/// bar title format.
/// </summary>
public enum MenuBarTitleFormat
{
    /// <summary>
    /// The track title alone, falling back to the artist when the title is empty.
    /// </summary>
    TitleOnly,

    /// <summary>
    /// Title, a dash, then artist.
    /// </summary>
    TitleArtist,

    /// <summary>
    /// Artist, a dash, then title.
    /// </summary>
    ArtistTitle,

    /// <summary>
    /// No text at all; the tray shows only the icon.
    /// </summary>
    Hidden,
}

/// <summary>
/// What the tray icon shows while a track is loaded.
/// </summary>
public enum MenuBarArtworkStyle
{
    /// <summary>
    /// The album cover, rounded to fit the panel.
    /// </summary>
    AlbumArtwork,

    /// <summary>
    /// The Reprise icon, regardless of the track.
    /// </summary>
    Hidden,
}

/// <summary>
/// User-facing names for the panel options, matching the macOS settings.
/// </summary>
public static class PanelOptionNames
{
    /// <summary>
    /// Returns the menu label for a theme.
    /// </summary>
    /// <param name="theme">Theme to name.</param>
    /// <returns>The label the macOS settings window uses.</returns>
    /// <example>
    /// <code>
    /// PanelOptionNames.DisplayName(PanelTheme.Liquid); // "Liquid"
    /// </code>
    /// </example>
    public static string DisplayName(this PanelTheme theme) => theme switch
    {
        PanelTheme.White => "화이트",
        PanelTheme.Dark => "다크",
        PanelTheme.Liquid => "Liquid",
        PanelTheme.System => "시스템 설정",
        _ => theme.ToString(),
    };

    /// <summary>
    /// Returns the menu label for a leading time style.
    /// </summary>
    /// <param name="style">Style to name.</param>
    /// <returns>The label the macOS settings window uses.</returns>
    public static string DisplayName(this PanelLeadingTimeStyle style) => style switch
    {
        PanelLeadingTimeStyle.Elapsed => "현재 재생",
        PanelLeadingTimeStyle.Zero => "00:00",
        _ => style.ToString(),
    };

    /// <summary>
    /// Returns the menu label for a trailing time style.
    /// </summary>
    /// <param name="style">Style to name.</param>
    /// <returns>The label the macOS settings window uses.</returns>
    public static string DisplayName(this PanelTrailingTimeStyle style) => style switch
    {
        PanelTrailingTimeStyle.Remaining => "남은 시간",
        PanelTrailingTimeStyle.Duration => "총 길이",
        _ => style.ToString(),
    };

    /// <summary>
    /// Returns the menu label for a title format.
    /// </summary>
    /// <param name="format">Format to name.</param>
    /// <returns>The label the macOS settings window uses.</returns>
    public static string DisplayName(this MenuBarTitleFormat format) => format switch
    {
        MenuBarTitleFormat.TitleOnly => "제목만",
        MenuBarTitleFormat.TitleArtist => "제목 - 아티스트",
        MenuBarTitleFormat.ArtistTitle => "아티스트 - 제목",
        MenuBarTitleFormat.Hidden => "없음",
        _ => format.ToString(),
    };

    /// <summary>
    /// Returns the menu label for an artwork style.
    /// </summary>
    /// <param name="style">Style to name.</param>
    /// <returns>The label the macOS settings window uses.</returns>
    public static string DisplayName(this MenuBarArtworkStyle style) => style switch
    {
        MenuBarArtworkStyle.AlbumArtwork => "앨범",
        MenuBarArtworkStyle.Hidden => "없음",
        _ => style.ToString(),
    };
}
